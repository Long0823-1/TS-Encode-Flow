using System;
using System.IO;
using System.Linq;
using TsEncodeFlow;
static partial class Tests {
 static void LinuxCmDelogoTests(string root) {
  // ffmpeg delogo: フィルタ文字列、元映像座標での適用位置、VA-APIではhwupload前段。
  var area=new LogoArea {X=100,Y=40,Width=200,Height=60};
  Check(Delogo.Filter(area)=="delogo=x=100:y=40:w=200:h=60","delogo filter string from logo area");
  var m=new Metadata {Title="番組",Channel="局",OriginalName="source",Width=720,Height=480,Sar=1,VideoIndex=0,AudioIndex=1,Fps=30000.0/1001,Duration=50};
  var filtered=Commands.Filters(new Preset {CropRight=20},m,area);
  Check(filtered.StartsWith("delogo=x=100:y=40:w=200:h=60,crop="),"delogo applies before crop in source coordinates");
  var s=new Settings {Ffmpeg=Store.FindTool("ffmpeg")};
  var vaapi=Commands.Vaapi(s,new Preset {Engine=Engine.Vaapi,Codec=Codec.H264},m,"in.ts","out.mp4",null,area);
  int vf=vaapi.Args.IndexOf("-vf");Check(vf>=0&&vaapi.Args[vf+1].StartsWith("delogo=")&&vaapi.Args[vf+1].EndsWith("format=nv12,hwupload"),"VA-API delogo runs in software before hwupload");
  var cpu=Commands.Ffmpeg(s,new Preset {Engine=Engine.CPU},m,"in.ts","out.mp4",delogo:area);
  int cvf=cpu.Args.IndexOf("-vf");Check(cvf>=0&&cpu.Args[cvf+1].StartsWith("delogo="),"CPU delogo is the first filter");
  // 領域解決: 登録済みロゴ範囲（.area.json）→ LGDヘッダー優先 → どちらもなければ明示的に失敗。
  string data=Path.Combine(root,"LinuxDelogo");Directory.CreateDirectory(data);
  var job=new Job {Input="in.ts",Metadata=m};
  StationLogos.SaveArea(data,job,area);
  var resolved=Delogo.ResolveArea(data,job,new Settings());
  Check(resolved.X==100&&resolved.Y==40&&resolved.Width==200&&resolved.Height==60,"delogo area resolves from registered station area");
  string lgd=Path.Combine(root,"fake.lgd");WriteFakeLgd(lgd,300,20,120,48);
  var fromLgd=Delogo.ResolveArea(data,job,new Settings {CmLogo=lgd});
  Check(fromLgd.X==300&&fromLgd.Y==20&&fromLgd.Width==120&&fromLgd.Height==48,"delogo area resolves from LGD header");
  string emptyData=Path.Combine(root,"LinuxDelogoEmpty");Directory.CreateDirectory(emptyData);
  bool areaThrew=false;try{Delogo.ResolveArea(emptyData,job,new Settings());}catch(LogoAreaRequiredException){areaThrew=true;}
  Check(areaThrew,"delogo without logo or area fails explicitly");
 }
 static void WriteFakeLgd(string path,int x,int y,int w,int h) {
  // LGD v0.1: 署名28バイト + 件数4バイト(1) + 予約32バイト + x/y/height/width(Int16) + 予約8バイト + 画素×12バイト = 80 + w*h*12。
  using var fs=new FileStream(path,FileMode.Create,FileAccess.Write);using var bw=new BinaryWriter(fs);
  var sig=new byte[28];System.Text.Encoding.ASCII.GetBytes("<logo data file ver0.1>").CopyTo(sig,0);bw.Write(sig);
  bw.Write(new byte[]{0,0,0,1});bw.Write(new byte[32]);
  bw.Write((short)x);bw.Write((short)y);bw.Write((short)h);bw.Write((short)w);bw.Write(new byte[8]);
  bw.Write(new byte[w*h*12]);
 }
}
