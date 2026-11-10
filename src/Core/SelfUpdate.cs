using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class UpdateState {
  public string Phase="",Tag="",ZipPath="",Staging="",Error="",Digest="";
  public DateTime Updated;
 }
 public static class SelfUpdate {
  static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(30)};
  static readonly SemaphoreSlim DownloadGate=new(1,1);
  static SelfUpdate(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("TS-Encode-Flow-Updater/2.0");}
  public static string StateFile(string data)=>Path.Combine(data,"update-state.json");
  public static string StagingRoot(string data)=>Path.Combine(data,"Updates");
  public static UpdateState Read(string data)=>Store.Load(StateFile(data),()=>new UpdateState());
  public static void Write(string data,UpdateState state){state.Updated=DateTime.Now;Store.Save(StateFile(data),state);}
  public static bool BlocksNewJobs(string data){string phase=Read(data).Phase;return phase=="armed"||phase=="applying";}
  public static async Task Verify(string zip,string digest,CancellationToken token) {
   if(digest==null||!System.Text.RegularExpressions.Regex.IsMatch(digest,@"\Asha256:[a-fA-F0-9]{64}\z"))throw new InvalidDataException("配布元のSHA-256がありません。検証できない更新は適用しません。");
   using var input=File.OpenRead(zip);string actual=Convert.ToHexString(await SHA256.HashDataAsync(input,token));
   if(!string.Equals(actual,digest.Substring(7),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新ZIPのSHA-256が配布情報と一致しません。");
  }
  public static void ValidateStage(string folder) {
   foreach(string file in new[]{"TS-Encode-Flow.exe","TS-Encode-Flow.dll","Worker/TS-Encode-Worker.exe"})if(!File.Exists(Path.Combine(folder,file)))throw new InvalidDataException("更新ZIPに必要なファイルがありません："+file);
   foreach(var file in Directory.EnumerateFileSystemEntries(folder,"*",SearchOption.AllDirectories))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新ファイルにリンクは使用できません。");
  }
  public static async Task<UpdateState> DownloadLatest(string data,Version current,CancellationToken token,Action<string> progress)=>await DownloadLatest(data,current,token,progress,Http);
  public static async Task<UpdateState> DownloadLatest(string data,Version current,CancellationToken token,Action<string> progress,HttpClient http) {
   await DownloadGate.WaitAsync(token);var state=new UpdateState();
   try {
    using var request=new HttpRequestMessage(HttpMethod.Get,Updates.ListUrl);request.Headers.UserAgent.ParseAdd("TS-Encode-Flow/"+current.ToString(3));request.Headers.Accept.ParseAdd("application/vnd.github+json");
    using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var candidate=Updates.PickLatest(json.RootElement);
    if(candidate==null||candidate.Version<=Updates.Normalize(current)){state.Phase="none";state.Error=candidate==null?"更新リリースが見つかりません。":"最新版です。";Write(data,state);return state;}
    var uri=new Uri(candidate.ZipUrl);
    if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/Long0823-1/TS-Encode-Flow/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("更新ファイルの配布元が不正です。");
    state.Tag=candidate.Tag;state.Digest=candidate.Digest;state.Phase="downloading";Write(data,state);
    string folder=Path.Combine(StagingRoot(data),"download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string zip=Path.Combine(folder,"update.zip");
    using(var download=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token)) {
     download.EnsureSuccessStatusCode();long total=download.Content.Headers.ContentLength??0,done=0;var last=DateTime.MinValue;
     await using var input=await download.Content.ReadAsStreamAsync(token);await using var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true);byte[] buffer=new byte[131072];int n;
     while((n=await input.ReadAsync(buffer,token))>0){await output.WriteAsync(buffer.AsMemory(0,n),token);done+=n;if(done>2L*1024*1024*1024)throw new InvalidDataException("更新ZIPが大きすぎます。");if((DateTime.UtcNow-last).TotalMilliseconds>300){progress?.Invoke(candidate.Tag+" をダウンロード中："+(total>0?(done*100/total)+"%":(done/1048576)+" MB"));last=DateTime.UtcNow;}}
    }
    progress?.Invoke("SHA-256を検証中…");await Verify(zip,candidate.Digest,token);
    string staging=Path.Combine(folder,"app");Directory.CreateDirectory(staging);long expanded=0;
    using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries) {
     token.ThrowIfCancellationRequested();expanded+=entry.Length;if(expanded>4L*1024*1024*1024)throw new InvalidDataException("更新の展開容量が大きすぎます。");
     if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("更新ZIPにリンクが含まれています。");
     string path=ToolInstaller.SafeEntry(staging,entry.FullName);
     if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,false);
    }
    ValidateStage(staging);state.Phase="staged";state.ZipPath=zip;state.Staging=staging;Write(data,state);progress?.Invoke(candidate.Tag+" の準備ができました。");return state;
   }catch(OperationCanceledException){state.Phase="error";state.Error="更新のダウンロードを中止しました。";Write(data,state);throw;}
   catch(Exception e){state.Phase="error";state.Error=e.Message;Write(data,state);return state;}
   finally{DownloadGate.Release();}
  }
  public static bool ApplyPending(string data,string appFolder,Version current=null) {
   var state=Read(data);if(state.Phase!="armed")return false;
   try {
    current??=System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version??new Version(0,0,0);
    if(!Updates.TryVersion(state.Tag,out var target)||target<=Updates.Normalize(current)){state.Phase="none";Write(data,state);return false;}
    string stage=Path.GetFullPath(state.Staging),root=Path.GetFullPath(StagingRoot(data))+Path.DirectorySeparatorChar;
    if(!stage.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新の準備先が不正です。");
    Verify(state.ZipPath,state.Digest,CancellationToken.None).GetAwaiter().GetResult();ValidateStage(stage);
    appFolder=Path.GetFullPath(appFolder).TrimEnd(Path.DirectorySeparatorChar);
    if(!File.Exists(Path.Combine(appFolder,"TS-Encode-Flow.exe")))throw new InvalidDataException("アプリのインストール先を確認できません。");
    string script=Path.Combine(StagingRoot(data),"apply-"+Guid.NewGuid().ToString("N")+".ps1");
    string Q(string value)=>"'"+value.Replace("'","''")+"'";
    var files=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories).Select(f=>new {Relative=Path.GetRelativePath(stage,f),Hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}).ToArray();
    string manifest=script+".json";File.WriteAllText(manifest,JsonSerializer.Serialize(files),Encoding.UTF8);
    string header="$src="+Q(stage)+"\n$dst="+Q(appFolder)+"\n$data="+Q(Path.GetFullPath(data))+"\n$manifest="+Q(manifest)+"\n$self="+Environment.ProcessId+"\n";
    File.WriteAllText(script,header+ApplyScript,new UTF8Encoding(true));state.Phase="applying";Write(data,state);
    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "+Processes.Quote(script)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=StagingRoot(data)});return true;
   }catch(Exception e){state.Phase="error";state.Error=e.Message;Write(data,state);return false;}
  }
  public const string ApplyScript="""
$ErrorActionPreference='Stop'
$stateFile=Join-Path $data 'update-state.json'
$log=Join-Path $data 'update-install.log'
$copied=New-Object System.Collections.Generic.List[object]
$backup=Join-Path $dst ('.update-backup-'+[guid]::NewGuid().ToString('N'))
function State($phase,$errorText){$s=Get-Content -LiteralPath $stateFile -Raw|ConvertFrom-Json;$s.Phase=$phase;$s.Error=$errorText;$s.Updated=[DateTime]::Now.ToString('o');$tmp=$stateFile+'.install.tmp';$s|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $tmp -Encoding UTF8;Move-Item -LiteralPath $tmp -Destination $stateFile -Force}
function Scoped($root,$relative){$full=[IO.Path]::GetFullPath((Join-Path $root $relative));$prefix=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\';if(!$full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Update path escaped its directory'};return $full}
try {
 $deadline=[DateTime]::UtcNow.AddMinutes(10)
 # Only processes launched from this installation are relevant. Never kill them.
 do {
  $processes=@(Get-CimInstance Win32_Process|Where-Object {$_.ProcessId -eq $self -or $_.ExecutablePath -eq (Join-Path $dst 'TS-Encode-Flow.exe') -or $_.ExecutablePath -eq (Join-Path $dst 'Worker\TS-Encode-Worker.exe')})
  if($processes.Count -eq 0){break}
  if([DateTime]::UtcNow -gt $deadline){throw 'The app or worker did not exit. No installed files were changed.'}
  Start-Sleep -Milliseconds 500
 }while($true)
 $entries=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
 # Validate every target and stage hash before the first mutation.
 foreach($entry in $entries){$source=Scoped $src $entry.Relative;$target=Scoped $dst $entry.Relative;if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Hash){throw 'Staged update changed'};$parent=Split-Path $target;while($parent -and $parent.Length -ge $dst.Length){if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked update target'};$parent=Split-Path $parent};if((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked update target'}}
 foreach($entry in $entries){$source=Scoped $src $entry.Relative;$target=Scoped $dst $entry.Relative;$old=Scoped $backup $entry.Relative;$existed=Test-Path -LiteralPath $target;if($existed){New-Item -ItemType Directory -Path (Split-Path $old) -Force|Out-Null;Copy-Item -LiteralPath $target -Destination $old};$copied.Add(@{Target=$target;Old=$old;Existed=$existed});New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null;Copy-Item -LiteralPath $source -Destination $target -Force}
 State 'installed' ''
 Start-Process -FilePath (Join-Path $dst 'TS-Encode-Flow.exe') -ArgumentList @('--data',('"'+$data+'"'),'--no-update') -WorkingDirectory $dst -WindowStyle Normal
}catch {
 $message=$_.Exception.Message
 for($i=$copied.Count-1;$i -ge 0;$i--){$item=$copied[$i];try{if($item.Existed){Copy-Item -LiteralPath $item.Old -Destination $item.Target -Force}else{Remove-Item -LiteralPath $item.Target -Force}}catch{$message+='; rollback: '+$_.Exception.Message}}
 State 'error' $message
 $message|Add-Content -LiteralPath $log -Encoding UTF8
 if(Test-Path -LiteralPath (Join-Path $dst 'TS-Encode-Flow.exe')){Start-Process -FilePath (Join-Path $dst 'TS-Encode-Flow.exe') -ArgumentList @('--data',('"'+$data+'"'),'--no-update') -WorkingDirectory $dst -WindowStyle Normal}
}
""";
 }
}
