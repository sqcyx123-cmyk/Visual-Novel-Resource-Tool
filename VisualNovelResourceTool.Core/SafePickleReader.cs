using System.Buffers.Binary;
using System.Text;

namespace VisualNovelResourceTool.Core;

// Parses only inert containers and scalar values used by Ren'Py indexes.
// GLOBAL/REDUCE/OBJ/INST and all executable pickle opcodes are deliberately rejected.
internal sealed class SafePickleReader
{
    private static readonly object Mark = new();
    private readonly Stream _stream;
    private readonly List<object?> _stack = [];
    private readonly Dictionary<int, object?> _memo = [];
    private readonly CancellationToken _token;
    private long _operations;

    public SafePickleReader(Stream stream,CancellationToken token=default)=>(_stream,_token)=(stream,token);

    public object? Read()
    {
        while (true)
        {
            var op = ReadByte();
            switch (op)
            {
                case 0x80: ReadByte(); break; // PROTO
                case 0x95: ReadUInt64(); break; // FRAME
                case (byte)'.': return Pop(); // STOP
                case (byte)'(': _stack.Add(Mark); break;
                case (byte)'}': _stack.Add(new Dictionary<object, object?>()); break;
                case (byte)']': _stack.Add(new List<object?>()); break;
                case (byte)')': _stack.Add(Array.Empty<object?>()); break;
                case (byte)'N': _stack.Add(null); break;
                case 0x88: _stack.Add(true); break;
                case 0x89: _stack.Add(false); break;
                case (byte)'K': _stack.Add((long)ReadByte()); break;
                case (byte)'M': _stack.Add((long)ReadUInt16()); break;
                case (byte)'J': _stack.Add((long)ReadInt32()); break;
                case 0x8a: _stack.Add(ReadLong(ReadByte())); break;
                case 0x8b: _stack.Add(ReadLong(checked((int)ReadUInt32()))); break;
                case (byte)'X': _stack.Add(ReadUtf8(checked((int)ReadUInt32()))); break;
                case 0x8c: _stack.Add(ReadUtf8(ReadByte())); break;
                case 0x8d: _stack.Add(ReadUtf8(checked((int)ReadUInt64()))); break;
                case (byte)'U': _stack.Add(ReadBytes(ReadByte())); break;
                case (byte)'T': _stack.Add(ReadBytes(checked((int)ReadUInt32()))); break;
                case (byte)'B': _stack.Add(ReadBytes(checked((int)ReadUInt32()))); break;
                case (byte)'C': _stack.Add(ReadBytes(ReadByte())); break;
                case 0x8e: _stack.Add(ReadBytes(checked((int)ReadUInt64()))); break;
                case (byte)'q': _memo[ReadByte()] = Peek(); break;
                case (byte)'r': _memo[ReadInt32()] = Peek(); break;
                case 0x94: _memo[_memo.Count] = Peek(); break;
                case (byte)'h': _stack.Add(GetMemo(ReadByte())); break;
                case (byte)'j': _stack.Add(GetMemo(ReadInt32())); break;
                case (byte)'a': AppendOne(); break;
                case (byte)'e': AppendMarked(); break;
                case (byte)'s': SetItem(); break;
                case (byte)'u': SetItems(); break;
                case (byte)'t': _stack.Add(PopMarked().ToArray()); break;
                case 0x85: _stack.Add(new[] { Pop() }); break;
                case 0x86: { var b = Pop(); var a = Pop(); _stack.Add(new[] { a, b }); break; }
                case 0x87: { var c = Pop(); var b = Pop(); var a = Pop(); _stack.Add(new[] { a, b, c }); break; }
                case (byte)'0': Pop(); break;
                case (byte)'1': PopMarked(); break;
                case (byte)'2': _stack.Add(Peek()); break;
                default: throw new InvalidDataException($"RPA 索引包含不安全或不支持的 Pickle 操作码 0x{op:X2}，已拒绝解析。");
            }
        }
    }

    private void AppendMarked() { var values = PopMarked(); ((List<object?>)Peek()!).AddRange(values); }
    private void AppendOne() { var value = Pop(); ((List<object?>)Peek()!).Add(value); }
    private void SetItem() { var value = Pop(); var key = Pop() ?? throw new InvalidDataException("索引键为空。"); ((Dictionary<object, object?>)Peek()!)[key] = value; }
    private void SetItems() { var values = PopMarked(); var dict = (Dictionary<object, object?>)Peek()!; for (var i = 0; i < values.Count; i += 2) dict[values[i] ?? throw new InvalidDataException("索引键为空。")] = values[i + 1]; }
    private object? Peek() => _stack.Count > 0 ? _stack[^1] : throw new InvalidDataException("Pickle 栈为空。");
    private object? Pop() { var value = Peek(); _stack.RemoveAt(_stack.Count - 1); return value; }
    private List<object?> PopMarked() { var at = _stack.LastIndexOf(Mark); if (at < 0) throw new InvalidDataException("Pickle MARK 缺失。"); var result = _stack.Skip(at + 1).ToList(); _stack.RemoveRange(at, _stack.Count - at); return result; }
    private object? GetMemo(int key) => _memo.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Pickle memo 引用无效。");
    private byte ReadByte() { if((++_operations&4095)==0)_token.ThrowIfCancellationRequested();if(_operations>20_000_000)throw new InvalidDataException("RPA 索引操作数量异常，已停止读取。");if(_stack.Count>1_000_000||_memo.Count>1_000_000)throw new InvalidDataException("RPA 索引容器数量异常，已停止读取。");var b = _stream.ReadByte(); return b < 0 ? throw new EndOfStreamException("RPA 索引意外结束。") : (byte)b; }
    private byte[] ReadBytes(int length) { _token.ThrowIfCancellationRequested();if (length < 0 || length > 256 * 1024 * 1024) throw new InvalidDataException("索引字段过大。"); var bytes = new byte[length];for(var offset=0;offset<length;){_token.ThrowIfCancellationRequested();var count=Math.Min(64*1024,length-offset);_stream.ReadExactly(bytes.AsSpan(offset,count));offset+=count;} return bytes; }
    private string ReadUtf8(int length) => Encoding.UTF8.GetString(ReadBytes(length));
    private ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));
    private uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));
    private int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4));
    private ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8));
    private long ReadLong(byte length) => ReadLong((int)length);
    private long ReadLong(int length)
    {
        if (length is < 0 or > 8) throw new InvalidDataException("超大整数不受支持。");
        var bytes = ReadBytes(length); long value = 0; for (var i = 0; i < length; i++) value |= (long)bytes[i] << (8 * i);
        if (length > 0 && (bytes[^1] & 0x80) != 0 && length < 8) value |= -1L << (length * 8);
        return value;
    }
}
