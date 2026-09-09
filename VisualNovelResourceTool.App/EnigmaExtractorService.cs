using System.Diagnostics;
using System.IO;
using VisualNovelResourceTool.Core;

namespace VisualNovelResourceTool.App;

internal static class EnigmaExtractorService
{
    public static async Task<ExtractionSummary> ExtractVisualsAsync(string executable,string output,bool overwrite,bool addChineseLabels,CancellationToken token)
    {
        var tool=Path.Combine(AppContext.BaseDirectory,"third_party","evbunpack","evbunpack.exe");
        if(!File.Exists(tool))throw new FileNotFoundException("缺少 EXE 解包组件 evbunpack.exe。",tool);
        var temp=Path.Combine(Path.GetTempPath(),"vnrt-evb-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--ignore-pe");start.ArgumentList.Add(executable);start.ArgumentList.Add(temp);
            var process = await ExternalProcess.RunAsync(start, token);if(process.ExitCode!=0)throw new InvalidDataException("EXE 解包失败："+(process.Error+Environment.NewLine+process.Output).Trim().Split('\n').LastOrDefault());
            var content=Directory.Exists(Path.Combine(temp,"content"))?Path.Combine(temp,"content"):temp;var paths=new SafeExtractionPath(output,addChineseLabels);var extracted=0;var skipped=0;var failed=0;long bytes=0;var failures=new List<string>();
            foreach(var file in GameFiles.Enumerate(content,token))
            {
                token.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(content,file).Replace('\\','/');if(!AssetClassifier.Matches(relative,AssetCategory.Images))continue;
                string? part=null;
                try
                {
                    var target=paths.Resolve(relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if(!overwrite&&File.Exists(target)){skipped++;continue;}
                    part=SafeExtractionPath.TemporaryPath(target);
                    await using(var input=File.OpenRead(file))
                    await using(var destination=new FileStream(part,FileMode.CreateNew,FileAccess.Write))
                        await input.CopyToAsync(destination,token);
                    File.Move(part,target,overwrite);extracted++;bytes+=new FileInfo(file).Length;
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){failed++;failures.Add($"{relative}: {ex.Message}");}
                finally{if(part is not null&&File.Exists(part))File.Delete(part);}
            }
            var renamed=0;var encrypted=RpgMakerProject.Open(content,token);if(encrypted.Entries.Any(e=>e.Encrypted)){var result=await encrypted.ExtractAsync(encrypted.Entries.Where(e=>e.Encrypted&&AssetClassifier.Matches(e.RelativeOutputPath,AssetCategory.Images)),output,overwrite,null,token,addChineseLabels);extracted+=result.Extracted;skipped+=result.Skipped;failed+=result.Failed;bytes+=result.BytesWritten;renamed+=result.Renamed;}
            paths.WriteReport(failures);return new(extracted,skipped,bytes,output,failed,paths.Renamed+renamed);
        }
        finally{try{var full=Path.GetFullPath(temp);var tempRoot=Path.GetFullPath(Path.GetTempPath());if(full.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("vnrt-evb-",StringComparison.Ordinal))Directory.Delete(full,true);}catch{} }
    }
}
