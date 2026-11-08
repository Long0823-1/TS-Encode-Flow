using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace TsEncodeFlow {
 public sealed class LogoOperationResult {public bool Success;public string Message="",FileName="";}
 public static class LogoLibrary {
  static string Folder(string data)=>Path.Combine(data,"Logos");
  static void Latest(string data,string key,string file) {
   string manifest=Path.Combine(Folder(data),key+".latest");
   if(file==null){if(File.Exists(manifest))File.Delete(manifest);return;}
   string name=Path.GetFileName(file),version=Path.GetFileNameWithoutExtension(file).Substring(key.Length+2),temp=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temp,int.Parse(version)+"\n"+name+"\n");File.Move(temp,manifest,true);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static string Import(string data,StationLogo station,string source,string fingerprint) {
   var geometry=StationLogos.Geometry(station);if(string.IsNullOrWhiteSpace(fingerprint)||Store.Fingerprint(source)!=fingerprint)throw new IOException("選択したLGDが変更されています。ファイルを選び直してください。");
   if(!new[]{".lgd",".lgd2"}.Contains(Path.GetExtension(source),StringComparer.OrdinalIgnoreCase))throw new ArgumentException("LGDまたはLGD2ファイルを選択してください。");
   string folder=Folder(data);Directory.CreateDirectory(folder);string stage=Path.Combine(folder,".import-"+Guid.NewGuid().ToString("N")+".tmp"),target=null;
   try {
    File.Copy(source,stage,false);if(Store.Fingerprint(source)!=fingerprint)throw new IOException("読込み中にLGDが変更されました。");var bitmap=LogoCandidates.Read(stage,geometry);
    var entry=new StationLogo {Key=station.Key,Channel=string.IsNullOrWhiteSpace(station.Channel)?StationLogos.Describe(station.Key).Channel:station.Channel.Trim(),Width=geometry.Width,Height=geometry.Height,Area=new LogoArea {X=bitmap.X,Y=bitmap.Y,Width=bitmap.Width,Height=bitmap.Height}};
    if(entry.Channel.Length>200||entry.Channel.Any(char.IsControl))throw new ArgumentException("放送局名は改行なし200文字以下で指定してください。");
    for(int version=1;version<=9999;version++){string candidate=Path.Combine(folder,station.Key+"-v"+version.ToString("0000")+".lgd");try{File.Move(stage,candidate);target=candidate;break;}catch(IOException)when(File.Exists(candidate)){}}
    if(target==null)throw new IOException("ロゴの保存名を確保できません。");
    Store.Save(Path.Combine(folder,station.Key+".area.json"),entry);Latest(data,station.Key,target);return target;
   }catch{if(target!=null&&File.Exists(target))File.Delete(target);throw;}
   finally{if(File.Exists(stage))File.Delete(stage);}
  }
  public static void Delete(string data,string key,string fileName,string fingerprint) {
   StationLogos.Describe(key);string target=StationLogos.Files(data,key).FirstOrDefault(p=>Path.GetFileName(p)==fileName);
   if(target==null)throw new IOException("この放送局の登録済みロゴが見つかりません。一覧を更新してください。");
   if(string.IsNullOrWhiteSpace(fingerprint)||Store.Fingerprint(target)!=fingerprint)throw new IOException("ロゴが変更されています。一覧を更新して確認してください。");
   bool current=string.Equals(StationLogos.Find(data,key),target,StringComparison.OrdinalIgnoreCase);string stage=target+".delete-"+Guid.NewGuid().ToString("N")+".tmp";
   File.Move(target,stage);
   try{if(current)Latest(data,key,StationLogos.Find(data,key));File.Delete(stage);}
   catch{if(File.Exists(stage)&&!File.Exists(target)){File.Move(stage,target);if(current)Latest(data,key,target);}throw;}
  }
  public static void DeleteStation(string data,string key) {
   StationLogos.Describe(key);string folder=Folder(data);
   var removed=new List<(string from,string to)>();string stage=null;
   try {
    foreach(string target in StationLogos.Files(data,key)) {string moved=target+".delete-"+Guid.NewGuid().ToString("N")+".tmp";File.Move(target,moved);removed.Add((moved,target));}
    string manifest=Path.Combine(folder,key+".latest");if(File.Exists(manifest)){stage=manifest+".delete-"+Guid.NewGuid().ToString("N")+".tmp";File.Move(manifest,stage);}
    foreach(var entry in removed)File.Delete(entry.from);if(stage!=null)File.Delete(stage);
    string area=Path.Combine(folder,key+".area.json");if(File.Exists(area))File.Delete(area);
   }catch{foreach(var entry in removed)if(File.Exists(entry.from)&&!File.Exists(entry.to))File.Move(entry.from,entry.to);if(stage!=null&&File.Exists(stage)&&!File.Exists(Path.Combine(folder,key+".latest")))File.Move(stage,Path.Combine(folder,key+".latest"));throw;}
  }
 }
}
