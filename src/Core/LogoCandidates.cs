using System;
using System.IO;
using System.Linq;
using System.Text;
namespace TsEncodeFlow {
 public sealed class LogoReviewException:Exception {public LogoReviewException():base("背景を再推定してロゴ候補を作成しました。番組テロップが混ざっていないか「生成ロゴを確認」で確認してください。確認前は局ロゴとして登録しません。") {}}
 public sealed class LogoBitmap {public int Width,Height;public byte[] Luma,Alpha;}
 public static class LogoCandidates {
  public static LogoBitmap Read(string path,Metadata source) {
   using var file=File.OpenRead(path);using var r=new BinaryReader(file);var signature=r.ReadBytes(28);
   if(signature.Length!=28||!Encoding.ASCII.GetString(signature).StartsWith("<logo data file ver0.1>"))throw new InvalidDataException("未対応のロゴ候補形式です。");
   var count=r.ReadBytes(4);if(!count.SequenceEqual(new byte[]{0,0,0,1}))throw new InvalidDataException("ロゴ候補は1件のLGDに限ります。");r.ReadBytes(32);
   int x=r.ReadInt16(),y=r.ReadInt16(),height=r.ReadInt16(),width=r.ReadInt16();r.ReadBytes(8);
   var area=new LogoArea {X=x,Y=y,Width=width,Height=height};area.Validate(source);long size=(long)width*height;
   if(size>10000000||file.Length!=80+size*12)throw new InvalidDataException("ロゴ候補のサイズが不正です。");
   var result=new LogoBitmap {Width=width,Height=height,Luma=new byte[(int)size],Alpha=new byte[(int)size]};
   for(int i=0;i<size;i++){int alpha=r.ReadInt16(),value=r.ReadInt16();r.ReadBytes(8);result.Alpha[i]=(byte)Math.Clamp((int)Math.Round(alpha*255d/1000),0,255);result.Luma[i]=(byte)Math.Clamp((int)Math.Round(value*219d/4096+16),0,255);}return result;
  }
  public static string Promote(string data,Job job) {
   string candidate=Path.GetFullPath(job.LogoCandidate??""),folder=Path.GetFullPath(Commercials.Folder(job,data))+Path.DirectorySeparatorChar;
   if(!candidate.StartsWith(folder,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("このジョブのロゴ候補ではありません。");Read(candidate,job.Metadata);
   string cache=Path.Combine(data,"Logos");Directory.CreateDirectory(cache);string key=StationLogos.Key(job),target=null;
   for(int i=1;i<=9999;i++){string p=Path.Combine(cache,key+"-v"+i.ToString("0000")+".lgd");try{File.Copy(candidate,p,false);target=p;break;}catch(IOException)when(File.Exists(p)){}}
   if(target==null)throw new IOException("ロゴの保存名を確保できません。");string manifest=Path.Combine(cache,key+".latest"),temp=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";
   string version=Path.GetFileNameWithoutExtension(target).Substring(key.Length+2);File.WriteAllText(temp,int.Parse(version)+"\n"+Path.GetFileName(target)+"\n");File.Move(temp,manifest,true);return target;
  }
 }
}
