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
            using var process=Process.Start(start)??throw new InvalidOperationException("无法启动 EXE 解包组件。");using var cancelRegistration=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch{}});var stdout=process.StandardOutput.ReadToEndAsync(token);var stderr=process.StandardError.ReadToEndAsync(token);await process.WaitForExitAsync(token);token.ThrowIfCancellationRequested();var diagnostic=(await stderr)+Environment.NewLine+(await stdout);if(process.ExitCode!=0)throw new InvalidDataException("EXE 解包失败："+diagnostic.Trim().Split('\n').LastOrDefault());
            var content=Directory.Exists(Path.Combine(temp,"content"))?Path.Combine(temp,"content"):temp;var paths=new SafeExtractionPath(output,addChineseLabels);var extracted=0;var skipped=0;var failed=0;long bytes=0;var failures=new List<string>();
            foreach(var file in Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(content,file).Replace('\\','/');if(!AssetClassifier.Matches(relative,AssetCategory.Images))continue;
                try{var target=paths.Resolve(relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);if(!overwrite&&File.Exists(target)){skipped++;continue;}var part=target+".vnrt-part";File.Copy(file,part,true);File.Move(part,target,true);extracted++;bytes+=new FileInfo(file).Length;}catch(Exception ex){failed++;failures.Add($"{relative}: {ex.Message}");}
            }
            var encrypted=RpgMakerProject.Open(content);if(encrypted.Entries.Count>0&&encrypted.EncryptionKey is not null){var result=await encrypted.ExtractAsync(encrypted.Entries.Where(e=>AssetClassifier.IsImage(e.RelativeOutputPath)),output,overwrite,null,token,addChineseLabels);extracted+=result.Extracted;skipped+=result.Skipped;failed+=result.Failed;bytes+=result.BytesWritten;}
            paths.WriteReport(failures);return new(extracted,skipped,bytes,output,failed,paths.Renamed);
        }
        finally{try{var full=Path.GetFullPath(temp);var tempRoot=Path.GetFullPath(Path.GetTempPath());if(full.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("vnrt-evb-",StringComparison.Ordinal))Directory.Delete(full,true);}catch{} }
    }
}
