using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class UpdateResult {
  public bool Available;
  public string Version="",Message="";
 }
 public static class Updates {
  public const string ReleasesUrl="https://github.com/Long0823-1/TS-Encode-Flow/releases";
  public const string ApiUrl="https://api.github.com/repos/Long0823-1/TS-Encode-Flow/releases/latest";
  public const string ListUrl="https://api.github.com/repos/Long0823-1/TS-Encode-Flow/releases?per_page=30";
  public const string ZipName="TS-Encode-Flow-win-x64.zip";
  // Semantic versions only (vX.Y.Z / X.Y.Z). Date-style CI tags (v2026.11.09.2)
  // and suffixed tags never take part in update resolution.
  public static bool TryVersion(string tag,out Version version) {
   version=null;
   if(string.IsNullOrWhiteSpace(tag))return false;
   string text=tag.Trim();if(text.StartsWith("v",StringComparison.OrdinalIgnoreCase))text=text.Substring(1);
   if(text.IndexOfAny(new[]{'-','+'})>=0)return false;
   if(!Version.TryParse(text,out var parsed))return false;
   if(parsed.Major>999||parsed.Minor>999)return false;
   version=new Version(parsed.Major,parsed.Minor,Math.Max(0,parsed.Build));return true;
  }
  public static Version Normalize(Version current)=>new Version(current.Major,current.Minor,Math.Max(0,current.Build));
  // One release-list entry that can serve as an update target.
  public sealed class Candidate {
   public string Tag="",ZipUrl="",Digest="";
   public Version Version;
   public DateTimeOffset Published;
   public bool Prerelease;
  }
  // Newest semantic release wins — prereleases included on purpose. The user
  // asked for "always bring me the newest"; during this project's rapid
  // iteration every build is effectively a prerelease anyway.
  public static Candidate PickLatest(JsonElement releases) {
   Candidate best=null;
   foreach(var release in releases.EnumerateArray()) {
    if(release.TryGetProperty("draft",out var draft)&&draft.ValueKind==JsonValueKind.True)continue;
    if(!release.TryGetProperty("tag_name",out var tagElement))continue;
    if(!TryVersion(tagElement.GetString(),out var version))continue;
    string zipUrl="",digest="";
    if(release.TryGetProperty("assets",out var assets)&&assets.ValueKind==JsonValueKind.Array) {
     foreach(var asset in assets.EnumerateArray()) {
      if(asset.TryGetProperty("name",out var name)&&name.GetString()==ZipName) {
       zipUrl=asset.TryGetProperty("browser_download_url",out var url)?url.GetString()??"":"";
       digest=asset.TryGetProperty("digest",out var d)?d.GetString()??"":"";
      }
     }
    }
    if(string.IsNullOrEmpty(zipUrl))continue;
    DateTimeOffset published=DateTimeOffset.MinValue;
    if(release.TryGetProperty("published_at",out var at)&&at.ValueKind==JsonValueKind.String)DateTimeOffset.TryParse(at.GetString(),out published);
    bool prerelease=release.TryGetProperty("prerelease",out var pre)&&pre.ValueKind==JsonValueKind.True;
    if(best==null||version>best.Version||(version==best.Version&&published>best.Published))
     best=new Candidate {Tag=tagElement.GetString(),Version=version,ZipUrl=zipUrl,Digest=digest,Published=published,Prerelease=prerelease};
   }
   return best;
  }
  static async Task<string> Get(HttpClient http,string url,Version current,CancellationToken token) {
   using var request=new HttpRequestMessage(HttpMethod.Get,url);
   request.Headers.UserAgent.ParseAdd("TS-Encode-Flow/"+current.ToString(3));
   request.Headers.Accept.ParseAdd("application/vnd.github+json");
   using var response=await http.SendAsync(request,token).ConfigureAwait(false);
   if(response.StatusCode==HttpStatusCode.NotFound)return null;
   if(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)throw new UpdateRateLimited();
   response.EnsureSuccessStatusCode();
   return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
  }
  sealed class UpdateRateLimited:Exception {}
  public static async Task<UpdateResult> Check(HttpClient http,Version current,CancellationToken cancellation=default) {
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(12));
   try {
    string body=await Get(http,ListUrl,current,timeout.Token).ConfigureAwait(false);
    if(body==null)return new UpdateResult {Message="更新リリースはまだありません。"};
    using var json=JsonDocument.Parse(body);
    var candidate=PickLatest(json.RootElement);
    var normalized=Normalize(current);
    if(candidate==null)return new UpdateResult {Message="更新リリースはまだありません。"};
    bool newer=candidate.Version>normalized;
    return new UpdateResult {Available=newer,Version=candidate.Tag,Message=newer?candidate.Tag+" が利用できます。アプリ内更新で自動的に入れ替えられます。":"最新版を使用しています（"+normalized.ToString(3)+"）。"};
   }catch(UpdateRateLimited){return new UpdateResult {Message="GitHubの利用制限により確認できません。時間をおいて再確認してください。"};}
   catch(OperationCanceledException){return new UpdateResult {Message="更新確認がタイムアウト、または中止されました。後で再確認できます。"};}
   catch(HttpRequestException){return new UpdateResult {Message="GitHubに接続できませんでした。通信状態を確認して再確認してください。"};}
   catch(Exception e)when(e is JsonException||e is InvalidOperationException||e is KeyNotFoundException){return new UpdateResult {Message="更新情報を読み取れませんでした。後で再確認できます。"};}
  }
 }
 // One-shot self-update state shared between the worker (download + stage) and
 // the GUI (arm the swap, relaunch). Files live in the data folder.
 public sealed class UpdateState {
  public string Phase="",Tag="",ZipPath="",Staging="",Error="";
  public DateTime Updated;
 }
 public static class SelfUpdate {
  static readonly HttpClient Http=new HttpClient {Timeout=TimeSpan.FromMinutes(30)};
  static SelfUpdate(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("TS-Encode-Flow-Updater/1.0");}
  public static string StateFile(string data)=>Path.Combine(data,"update-state.json");
  public static string StagingRoot(string data)=>Path.Combine(data,"Updates");
  public static UpdateState Read(string data)=>Store.Load(StateFile(data),()=>new UpdateState());
  public static void Write(string data,UpdateState state){state.Updated=DateTime.Now;Store.Save(StateFile(data),state);}
  // Downloads the newest release zip, verifies the GitHub-published SHA-256
  // when present, and extracts it under <data>/Updates/<tag>/. Never touches
  // the installed app folder — the swap happens in ApplyPending at GUI start.
  public static async Task<UpdateState> DownloadLatest(string data,Version current,CancellationToken token,Action<string> progress) {
   var state=new UpdateState();
   try {
    string body;
    using(var request=new HttpRequestMessage(HttpMethod.Get,Updates.ListUrl)) {
     request.Headers.UserAgent.ParseAdd("TS-Encode-Flow/"+current.ToString(3));
     request.Headers.Accept.ParseAdd("application/vnd.github+json");
     using var response=await Http.SendAsync(request,token).ConfigureAwait(false);
     response.EnsureSuccessStatusCode();
     body=await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }
    Updates.Candidate candidate;
    using(var json=JsonDocument.Parse(body))candidate=Updates.PickLatest(json.RootElement);
    if(candidate==null){state.Phase="none";state.Error="更新リリースが見つかりません。";Write(data,state);return state;}
    if(candidate.Version<=Updates.Normalize(current)){state.Phase="none";state.Tag=candidate.Tag;state.Error="最新版です。";Write(data,state);return state;}
    string folder=Path.Combine(StagingRoot(data),candidate.Tag);Directory.CreateDirectory(folder);
    string zip=Path.Combine(folder,"update.zip");
    progress?.Invoke(candidate.Tag+" をダウンロード中…");
    if(!File.Exists(zip)) {
     string temp=zip+".part";
     if(File.Exists(temp))File.Delete(temp);
     using(var response=await Http.GetAsync(candidate.ZipUrl,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)) {
      response.EnsureSuccessStatusCode();
      await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
      await using var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true);
      await input.CopyToAsync(output,token).ConfigureAwait(false);
     }
     File.Move(temp,zip);
    }
    if(!string.IsNullOrEmpty(candidate.Digest)&&candidate.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)) {
     using var stream=File.OpenRead(zip);
     string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).ToLowerInvariant();
     if(!string.Equals(candidate.Digest.Substring(7),hash,StringComparison.OrdinalIgnoreCase)) {File.Delete(zip);throw new InvalidDataException("更新ZIPのSHA-256が配布情報と一致しません。");}
    }
    string staging=Path.Combine(folder,"app");
    if(Directory.Exists(staging))Directory.Delete(staging,true);
    Directory.CreateDirectory(staging);
    using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries) {
     string path=ToolInstaller.SafeEntry(staging,entry.FullName);
     if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,true);
    }
    if(!File.Exists(Path.Combine(staging,"TS-Encode-Flow.exe")))throw new InvalidDataException("更新ZIPにアプリ本体が含まれていません。");
    state.Phase="staged";state.Tag=candidate.Tag;state.ZipPath=zip;state.Staging=staging;state.Error="";
    Write(data,state);
    progress?.Invoke(candidate.Tag+" の準備ができました。");
    return state;
   }catch(OperationCanceledException){throw;}
   catch(Exception e){state.Phase="error";state.Error=e.Message;Write(data,state);return state;}
  }
  // Runs at GUI startup, before the worker is woken. If a swap is armed, a
  // detached PowerShell script waits for this process (and any worker from the
  // install folder) to exit, replaces the app folder contents with the staged
  // build (the Tools folder is preserved — downloaded tools live there), then
  // relaunches the GUI. Never throws.
  public static bool ApplyPending(string data,string appFolder) {
   try {
    var state=Read(data);
    if(state.Phase!="armed"||string.IsNullOrEmpty(state.Staging)||!File.Exists(Path.Combine(state.Staging,"TS-Encode-Flow.exe")))return false;
    if(!Updates.TryVersion(state.Tag,out var target)||target<=Updates.Normalize(typeof(SelfUpdate).Assembly.GetName().Version??new Version(0,0,0))) {state.Phase="none";Write(data,state);return false;}
    string script=Path.Combine(StagingRoot(data),"apply-update.ps1");
    Directory.CreateDirectory(StagingRoot(data));
    string exe=Path.Combine(appFolder,"TS-Encode-Flow.exe");
    var b=new StringBuilder();
    b.AppendLine("$ErrorActionPreference='SilentlyContinue'");
    b.AppendLine("$src='"+state.Staging.Replace("'","''")+"'");
    b.AppendLine("$dst='"+appFolder.Replace("'","''")+"'");
    b.AppendLine("$exe='"+exe.Replace("'","''")+"'");
    b.AppendLine("$self="+Environment.ProcessId);
    b.AppendLine("foreach($id in @($self)+@(Get-Process -Name 'TS-Encode-Flow','TS-Encode-Worker' -ErrorAction SilentlyContinue | ForEach-Object Id)){ try{ Wait-Process -Id $id -Timeout 120 -ErrorAction SilentlyContinue }catch{} }");
    b.AppendLine("Start-Sleep -Seconds 1");
    b.AppendLine("Get-ChildItem -LiteralPath $dst | Where-Object { $_.Name -ne 'Tools' } | Remove-Item -Recurse -Force");
    b.AppendLine("Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force");
    b.AppendLine("Start-Process -FilePath $exe -WorkingDirectory $dst");
    File.WriteAllText(script,b.ToString(),new UTF8Encoding(true));
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "+Processes.Quote(script)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=appFolder});
    state.Phase="applying";Write(data,state);
    return true;
   }catch{return false;}
  }
 }
}
