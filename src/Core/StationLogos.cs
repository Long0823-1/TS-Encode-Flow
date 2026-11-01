using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class LogoAreaRequiredException:Exception {public LogoAreaRequiredException():base("この放送局のロゴが未登録です。「ロゴ範囲を指定」で、ロゴが見える場面を囲んでください。") {}}
 public sealed class LogoArea {
  public int X,Y,Width,Height;
  public void Validate(Metadata m) {if(m==null||m.Width<1||m.Height<1||X<0||Y<0||Width<8||Height<8||(long)X+Width>m.Width||(long)Y+Height>m.Height)throw new ArgumentException("ロゴ範囲は元映像内の幅・高さ8ピクセル以上で指定してください。");}
  public override string ToString()=>X+","+Y+","+Width+","+Height;
  public static LogoArea FromSelection(double x1,double y1,double x2,double y2,int width,int height) {
   if(new[]{x1,y1,x2,y2}.Any(x=>!double.IsFinite(x)))throw new ArgumentException("範囲が不正です。");
   int left=(int)Math.Floor(Math.Clamp(Math.Min(x1,x2),0,1)*width),top=(int)Math.Floor(Math.Clamp(Math.Min(y1,y2),0,1)*height);
   return new LogoArea {X=left,Y=top,Width=(int)Math.Ceiling(Math.Clamp(Math.Max(x1,x2),0,1)*width)-left,Height=(int)Math.Ceiling(Math.Clamp(Math.Max(y1,y2),0,1)*height)-top};
  }
 }
 public sealed class StationLogo {public string Key,Channel;public int Width,Height;public LogoArea Area;}
 public static class StationLogos {
  public static string Key(Job job) {
   var m=job.Metadata??throw new ArgumentException("入力映像の情報がありません。");
   string key=m.NetworkId+"-"+m.TransportId+"-"+m.ServiceId+"-"+m.Width+"x"+m.Height;
   if(m.NetworkId==0||m.TransportId==0||m.ServiceId==0)key+="-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(m.Channel)?Path.GetFullPath(job.Input).ToUpperInvariant():m.Channel.Normalize()))).Substring(0,16);
   return key;
  }
  static string Folder(string data)=>Path.Combine(data,"Logos");
  public static string Find(string data,Job job) {
   string folder=Folder(data),key=Key(job),manifest=Path.Combine(folder,key+".latest");
   if(File.Exists(manifest)) {
    string[] lines=File.ReadAllLines(manifest);string name=lines.Length>1?lines[1].Trim():"";
    if(name==Path.GetFileName(name)&&name.StartsWith(key+"-v",StringComparison.Ordinal)&&name.EndsWith(".lgd",StringComparison.OrdinalIgnoreCase)) {string path=Path.Combine(folder,name);if(File.Exists(path)&&new FileInfo(path).Length>0)return path;}
   }
   if(!Directory.Exists(folder))return null;
   return Directory.EnumerateFiles(folder,key+"-v????.lgd").Where(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Substring(key.Length+2),out _)&&new FileInfo(p).Length>0).OrderByDescending(p=>p,StringComparer.Ordinal).FirstOrDefault();
  }
  public static StationLogo Read(string data,Job job)=>Store.Load(Path.Combine(Folder(data),Key(job)+".area.json"),()=>new StationLogo {Key=Key(job),Channel=job.Metadata.Channel,Width=job.Metadata.Width,Height=job.Metadata.Height});
  public static List<StationLogo> List(string data) {
   var result=new Dictionary<string,StationLogo>(StringComparer.Ordinal);string folder=Folder(data);if(!Directory.Exists(folder))return result.Values.ToList();
   foreach(string path in Directory.EnumerateFiles(folder,"*.area.json")) {var entry=Store.Load<StationLogo>(path,()=>new StationLogo());if(entry!=null&&entry.Key==Path.GetFileName(path).Replace(".area.json",""))result[entry.Key]=entry;}
   foreach(string path in Directory.EnumerateFiles(folder,"*-v????.lgd")) {string key=Path.GetFileNameWithoutExtension(path);key=key.Substring(0,key.Length-6);if(!result.ContainsKey(key))result[key]=new StationLogo {Key=key,Channel="登録済み局（局ID："+key+"）"};}
   return result.Values.OrderBy(e=>e.Channel).ToList();
  }
  public static void SaveArea(string data,Job job,LogoArea area) {area.Validate(job.Metadata);Store.Save(Path.Combine(Folder(data),Key(job)+".area.json"),new StationLogo {Key=Key(job),Channel=job.Metadata.Channel,Width=job.Metadata.Width,Height=job.Metadata.Height,Area=area});}
  public static bool CanConfigure(string data,Job job,Settings settings)=>job.Metadata!=null&&(job.State==State.NeedsLogo||job.State==State.Failed||job.State==State.Cancelled)&&job.Preset.CmMode!=CmMode.Off&&job.Preset.CmSource==CmSource.Amatsukaze&&!job.Preset.CmNoLogo&&string.IsNullOrWhiteSpace(settings.CmLogo)&&Find(data,job)==null;
  public static void Require(string data,Job job,Settings settings) {
   if(job.Preset.CmNoLogo||!string.IsNullOrWhiteSpace(settings.CmLogo)||Find(data,job)!=null)return;
   var area=Read(data,job).Area;if(area==null)throw new LogoAreaRequiredException();area.Validate(job.Metadata);
  }
  public static async Task<FileStream> Lock(string data,Job job,CancellationToken token) {
   Directory.CreateDirectory(Folder(data));string path=Path.Combine(Folder(data),Key(job)+".lock");
   for(;;){token.ThrowIfCancellationRequested();try{return File.Open(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){await Task.Delay(200,token);}}
  }
 }
}
