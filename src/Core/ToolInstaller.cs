using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed record ToolPackage(string Id,string Name,string Repo,string Pattern,string Executable,string Setting,string License,string Release="") {
  public string Source=>"https://github.com/"+Repo+(Id=="logoframe"?"/tree/"+Release+"/third_party/CmLogo":"");
 }
 public sealed class ToolReceipt {
  public string Version {get;set;}="";
  public string Asset {get;set;}="";
  public string Sha256 {get;set;}="";
  public string Path {get;set;}="";
  public bool UpstreamDigestVerified {get;set;}
  public string LicenseNotice {get;set;}="";
 }
 public static class ToolInstaller {
  public static readonly ToolPackage[] Packages={
   new("ffmpeg","FFmpeg / ffprobe","BtbN/FFmpeg-Builds",@"^ffmpeg-n9\.0-latest-win64-gpl-9\.0\.zip$","ffmpeg.exe","Ffmpeg","GPLビルド。付属ライブラリの条件も配布文書を参照。"),
   new("nvenc","NVEncC · NVIDIA","rigaya/NVEnc",@"^NVEncC_.*_x64\.7z$","NVEncC64.exe","Nvenc","本体・同梱ライブラリで条件が異なります。配布文書を参照。"),
   new("vceenc","VCEEncC · AMD","rigaya/VCEEnc",@"^VCEEncC_.*_x64\.7z$","VCEEncC64.exe","Vceenc","本体・同梱ライブラリで条件が異なります。配布文書を参照。"),
   new("qsvenc","QSVEncC · Intel","rigaya/QSVEnc",@"^QSVEncC_.*_x64\.7z$","QSVEncC64.exe","Qsvenc","本体・同梱ライブラリで条件が異なります。配布文書を参照。"),
   new("cm-components","CM解析 · chapter_exe / join_logo_scp / AviSynth","rigaya/Amatsukaze",@"^Amatsukaze_[\d.]+\.7z$","chapter_exe.exe","ChapterExe","CM解析に必要な構成物のみを展開。Amatsukaze本体・GUI・エンコーダーは導入しません。GPL等、各構成物の条件を確認できます。"),
   new("logoframe","CM解析 · logoframe（ロゴ検出）","Long0823-1/TS-Encode-Flow",@"^TS-Encode-Flow-CM-Logo-win-x64\.zip$","logoframe.exe","LogoFrame","GPL-3.0。tobitti0/logoframeをWindows x64向けにビルド。ソースコード・変更内容・ライセンスを同梱。","cm-logo-v2"),
   new("edcb","EDCB · 番組情報DLL","xtne6f/EDCB",@"^EDCB-work-plus-s-bin\.zip$","EpgDataCap3.dll","EpgDll","EDCB原作・改変部・構成ライブラリの個別条件が適用されます。原作READMEと配布元の文書を確認してください。")
  };
  static readonly HttpClient Http=CreateClient();
  static HttpClient CreateClient(){var h=new HttpClient {Timeout=TimeSpan.FromMinutes(30)};h.DefaultRequestHeaders.UserAgent.ParseAdd("TS-Encode-Flow-ToolSetup/1.0");return h;}
  public static string Root(string data)=>System.IO.Path.GetFullPath(System.IO.Path.Combine(data,"Tools"));
  public static ToolReceipt Installed(string data,ToolPackage tool)=>Store.Load(System.IO.Path.Combine(Root(data),tool.Id,"installed.json"),()=>new ToolReceipt());
  static async Task<(string Version,string Name,string Url,string Digest)> Resolve(string repo,string pattern,CancellationToken token,string release="") {
   using var response=await Http.GetAsync("https://api.github.com/repos/"+repo+(string.IsNullOrEmpty(release)?"/releases/latest":"/releases/tags/"+Uri.EscapeDataString(release)),token);response.EnsureSuccessStatusCode();
   using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
   var matches=json.RootElement.GetProperty("assets").EnumerateArray().Where(x=>System.Text.RegularExpressions.Regex.IsMatch(x.GetProperty("name").GetString(),pattern)).ToList();
   if(matches.Count!=1)throw new InvalidDataException(repo+"：対応する配布ファイルを一意に選べません。配布元を確認してください。");
   var a=matches[0];return(json.RootElement.GetProperty("tag_name").GetString(),a.GetProperty("name").GetString(),a.GetProperty("browser_download_url").GetString(),a.TryGetProperty("digest",out var d)?d.GetString():null);
  }
  static async Task<string> Download(string url,string file,string digest,CancellationToken token,Action<string> progress) {
   if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com")throw new InvalidDataException("許可されていない配布URLです。");
   using var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
   long total=response.Content.Headers.ContentLength??0,done=0;var last=DateTime.MinValue;
   await using(var input=await response.Content.ReadAsStreamAsync(token))await using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)){
    byte[] buffer=new byte[131072];int n;while((n=await input.ReadAsync(buffer,token))>0){await output.WriteAsync(buffer.AsMemory(0,n),token);done+=n;if((DateTime.UtcNow-last).TotalMilliseconds>400){progress($"ダウンロード {done/1048576:N0} / {(total>0?(total/1048576).ToString("N0"):"?")} MB");last=DateTime.UtcNow;}}
   }
   using var stream=File.OpenRead(file);string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).ToLowerInvariant();
   if(!string.IsNullOrEmpty(digest)&&(!digest.StartsWith("sha256:",StringComparison.Ordinal)||!string.Equals(digest.Substring(7),hash,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("配布元のSHA-256と一致しません。");return hash;
  }
  public static string SafeEntry(string destination,string entry) {
   string root=System.IO.Path.GetFullPath(destination)+System.IO.Path.DirectorySeparatorChar;
   string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(destination,entry));
   if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||entry.Contains(':'))throw new InvalidDataException("展開先の範囲外のパスです："+entry);return path;
  }
  static async Task<string> Extractor(string data,CancellationToken token,Action<string> progress) {
   string root=System.IO.Path.Combine(Root(data),"7zip");Directory.CreateDirectory(root);string exe=System.IO.Path.Combine(root,"7zr.exe");if(File.Exists(exe))return exe;
   var asset=await Resolve("ip7z/7zip",@"^7zr\.exe$",token);string temp=exe+"."+Guid.NewGuid().ToString("N");
   try{string hash=await Download(asset.Url,temp,asset.Digest,token,progress);File.Move(temp,exe,true);Store.Save(System.IO.Path.Combine(root,"installed.json"),new ToolReceipt {Version=asset.Version,Asset=asset.Url,Sha256=hash,Path=exe,UpstreamDigestVerified=!string.IsNullOrEmpty(asset.Digest)});return exe;}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  static readonly SemaphoreSlim InstallGate=new SemaphoreSlim(1,1);
  public static async Task<ToolReceipt> Install(string data,ToolPackage tool,CancellationToken token,Action<string> progress) {
   progress("準備しています…");await InstallGate.WaitAsync(token);try{return await InstallCore(data,tool,token,progress);}finally{InstallGate.Release();}
  }
  static async Task<ToolReceipt> InstallCore(string data,ToolPackage tool,CancellationToken token,Action<string> progress) {
   var asset=await Resolve(tool.Repo,tool.Pattern,token,tool.Release);string parent=System.IO.Path.Combine(Root(data),tool.Id);Directory.CreateDirectory(parent);
   var installed=Installed(data,tool);if(installed.Version==asset.Version&&File.Exists(installed.Path)&&!string.IsNullOrEmpty(asset.Digest)&&asset.Digest=="sha256:"+installed.Sha256){progress("導入済みの最新版を適用します。");installed.LicenseNotice=await FetchDocs(tool,asset.Version,PackageRoot(installed.Path),token);Store.Save(System.IO.Path.Combine(parent,"installed.json"),installed);return installed;}
   string stage=System.IO.Path.Combine(parent,"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   string archive=System.IO.Path.Combine(stage,asset.Name),contents=System.IO.Path.Combine(stage,"package");Directory.CreateDirectory(contents);
   try {
    string hash=await Download(asset.Url,archive,asset.Digest,token,progress);progress("展開しています…");
    if(asset.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {
     using var zip=ZipFile.OpenRead(archive);foreach(var entry in zip.Entries){token.ThrowIfCancellationRequested();string path=SafeEntry(contents,entry.FullName);if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));entry.ExtractToFile(path);}
    }else {
     string extractor=await Extractor(data,token,progress);var listing=await Processes.Run(extractor,new[]{"l","-slt","-sccUTF-8",archive},token);
     if(listing.ExitCode!=0)throw new InvalidDataException("7z一覧の読み込みに失敗しました。");
     var archivePaths=new List<string>();bool entries=false;foreach(string line in listing.Output.Split('\n')){string value=line.TrimEnd('\r');if(value=="----------"){entries=true;continue;}if(entries&&value.StartsWith("Path = ")){SafeEntry(contents,value.Substring(7));archivePaths.Add(value.Substring(7));}if(entries&&(value.StartsWith("Symbolic Link = ")||value.StartsWith("Hard Link = ")))throw new InvalidDataException("リンクを含むアーカイブは展開できません。");}
     if(!entries)throw new InvalidDataException("7zの内容を検証できません。");
     var extraction=new List<string>{"x","-y","-o"+contents,archive};
     if(tool.Id=="cm-components") {string include=System.IO.Path.Combine(stage,"cm-files.txt");File.WriteAllLines(include,archivePaths.Where(IsCmDependency),new UTF8Encoding(false));extraction.Add("-i@"+include);extraction.Add("-scsUTF-8");}
     var result=await Processes.Run(extractor,extraction,token);if(result.ExitCode!=0)throw new InvalidDataException("7zの展開に失敗しました。");
    }
    var found=Directory.GetFiles(contents,tool.Executable,SearchOption.AllDirectories).Where(IsX64).ToList();if(found.Count!=1)throw new InvalidDataException("x64版の "+tool.Executable+" を一意に検出できません。");
    string licenseNotice=await FetchDocs(tool,asset.Version,contents,token);
    string relative=System.IO.Path.GetRelativePath(contents,found[0]);string destination=System.IO.Path.Combine(parent,"package-"+Guid.NewGuid().ToString("N"));Directory.Move(contents,destination);
    var receipt=new ToolReceipt {Version=asset.Version,Asset=asset.Url,Sha256=hash,Path=System.IO.Path.Combine(destination,relative),UpstreamDigestVerified=!string.IsNullOrEmpty(asset.Digest),LicenseNotice=licenseNotice};
    Store.Save(System.IO.Path.Combine(parent,"installed.json"),receipt);return receipt;
   }finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
  }
  static string PackageRoot(string path) {var dir=new DirectoryInfo(System.IO.Path.GetDirectoryName(path));while(dir!=null&&!dir.Name.StartsWith("package-",StringComparison.Ordinal))dir=dir.Parent;if(dir==null)throw new InvalidDataException("導入先の情報が不正です。");return dir.FullName;}
  static async Task<string> FetchDocs(ToolPackage tool,string version,string folder,CancellationToken token) {
   if(tool.Id=="logoframe")return "";string notice="";foreach(string document in (tool.Id=="edcb"?new[]{"readme","contents/Document/Readme.txt"}:new[]{"readme","license"})) {
    string file=System.IO.Path.Combine(folder,document.Contains("/")?"UPSTREAM-EDCB-original-Readme.txt":"UPSTREAM-"+document+".txt");if(File.Exists(file))continue;
    try{using var response=await Http.GetAsync("https://api.github.com/repos/"+tool.Repo+"/"+document+"?ref="+Uri.EscapeDataString(version),token);
     if(response.IsSuccessStatusCode){using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));if(json.RootElement.TryGetProperty("content",out var content))File.WriteAllBytes(file,Convert.FromBase64String(content.GetString()));}
     else if(response.StatusCode!=System.Net.HttpStatusCode.NotFound)notice="配布元文書の追加取得に失敗しました。同梱文書と配布元を確認してください。";
    }catch(OperationCanceledException){throw;}catch(Exception){notice="配布元文書の追加取得に失敗しました。同梱文書と配布元を確認してください。";}
   }return notice;
  }
  public static bool IsCmDependency(string path) {
   string name=System.IO.Path.GetFileName(path),lower=name.ToLowerInvariant();
   var allowed=new[]{"chapter_exe.exe","join_logo_scp.exe","AviSynth.dll","LSMASHSource.dll","vcruntime140.dll","msvcp140.dll"};
   bool script=path.Replace('\\','/').Split('/').Contains("JL")&&name.EndsWith(".txt",StringComparison.OrdinalIgnoreCase);
   bool legal=lower.Contains("readme")||lower.Contains("license")||lower.Contains("copying")||lower.Contains("notice")||lower.Contains("copyright");
   return allowed.Contains(name,StringComparer.OrdinalIgnoreCase)||script||legal;
  }
  static bool IsX64(string file) {using var stream=File.OpenRead(file);using var r=new BinaryReader(stream);if(stream.Length<64||r.ReadUInt16()!=0x5a4d)return false;stream.Position=60;int offset=r.ReadInt32();if(offset<0||offset>stream.Length-6)return false;stream.Position=offset;return r.ReadUInt32()==0x4550&&r.ReadUInt16()==0x8664;}
  public static void Apply(Settings settings,ToolPackage tool,ToolReceipt receipt) {
   if(!File.Exists(receipt.Path))throw new FileNotFoundException("導入済みツールがありません。",receipt.Path);
   typeof(Settings).GetProperty(tool.Setting).SetValue(settings,receipt.Path);
   if(tool.Id=="ffmpeg"){string probe=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(receipt.Path),"ffprobe.exe");if(!File.Exists(probe))throw new FileNotFoundException("ffprobeがありません。",probe);settings.Ffprobe=probe;}
  }
  public static string[] LicenseFiles(string data,ToolPackage tool) {
   var receipt=Installed(data,tool);if(!File.Exists(receipt.Path))return Array.Empty<string>();
   var directory=new DirectoryInfo(System.IO.Path.GetDirectoryName(receipt.Path));while(directory!=null&&!directory.Name.StartsWith("package-",StringComparison.Ordinal))directory=directory.Parent;
   if(directory==null)return Array.Empty<string>();string root=directory.FullName;
   return Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>tool.Id!="logoframe"||p!=System.IO.Path.Combine(root,"UPSTREAM-license.txt")).Where(p=>{string n=System.IO.Path.GetFileName(p).ToLowerInvariant();return n.Contains("license")||n.Contains("copying")||n.Contains("copyright")||n.Contains("readme")||n.Contains("notice");}).Where(p=>new FileInfo(p).Length<4*1024*1024).ToArray();
  }
 }
}
