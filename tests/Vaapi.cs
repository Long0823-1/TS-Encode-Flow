using System;
using System.IO;
using System.Linq;
using TsEncodeFlow;
static partial class Tests {
 static void VaapiTests(string root) {
  var m=new Metadata {Title="番組",Channel="局",OriginalName="source",Width=1920,Height=1080,Sar=1,VideoIndex=0,AudioIndex=1};
  var s=new Settings {Ffmpeg=Store.FindTool("ffmpeg")};
  Check((int)Engine.CPU==0&&(int)Engine.NVEncC==1&&(int)Engine.VCEEncC==2&&(int)Engine.QSVEncC==3&&(int)Engine.Vaapi==4,"VA-API engine appended without renumbering existing engines");
  Check(PresetCatalog.EngineName(Engine.Vaapi)=="VA-API","VA-API engine display name");
  var catalog=PresetCatalog.Create();Check(catalog.Count==48,"base catalog stays at 48 presets on every platform");
  var vaapi=PresetCatalog.CreateVaapi();
  Check(vaapi.Count==(OperatingSystem.IsWindows()?0:11),"VA-API factory presets only on non-Windows: "+vaapi.Count);
  Check(vaapi.All(p=>p.Engine==Engine.Vaapi&&p.Codec!=Codec.AV1&&p.Speed==""),"VA-API presets use H.264/HEVC only");
  Check(vaapi.Select(p=>p.FactoryId).Distinct().Count()==vaapi.Count&&!vaapi.Any(p=>catalog.Any(c=>c.FactoryId==p.FactoryId)),"VA-API factory ids are distinct from the base catalog");
  foreach(var p in vaapi)Commands.Validate(p);
  var upgraded=new Settings {Presets=PresetCatalog.Create()};PresetCatalog.Upgrade(upgraded);
  Check(upgraded.Presets.Count==48+vaapi.Count,"settings upgrade registers VA-API presets on this platform only");
  Reject(()=>Commands.Validate(new Preset {Engine=Engine.Vaapi,Codec=Codec.AV1}),"VA-API preset rejects AV1");
  var h264=Commands.Vaapi(s,new Preset {Engine=Engine.Vaapi,Codec=Codec.H264,Quality=23,Deinterlace=Deinterlace.Yadif},m,"in.ts","out.mp4");
  Check(h264.Exe==s.Ffmpeg,"VA-API command runs ffmpeg");
  int dev=h264.Args.IndexOf("-vaapi_device");Check(dev>=0&&h264.Args[dev+1]=="/dev/dri/renderD128"&&dev<h264.Args.IndexOf("-i"),"VA-API device passed before input");
  int vf=h264.Args.IndexOf("-vf");Check(vf>=0&&h264.Args[vf+1].Contains("yadif")&&h264.Args[vf+1].EndsWith("format=nv12,hwupload"),"software filters run before hwupload");
  int qp=h264.Args.IndexOf("-qp");Check(h264.Args.Contains("h264_vaapi")&&qp>=0&&h264.Args[qp+1]=="23","H.264 VA-API quality maps to constant QP");
  Check(!h264.Args.Contains("-pix_fmt")&&!h264.Args.Contains("-preset"),"VA-API command omits CPU-only pix_fmt and preset");
  var hevc=Commands.Vaapi(s,new Preset {Engine=Engine.Vaapi,Codec=Codec.HEVC,Quality=26},m,"in.ts","out.mp4");
  int tag=hevc.Args.IndexOf("-tag:v");Check(hevc.Args.Contains("hevc_vaapi")&&tag>=0&&hevc.Args[tag+1]=="hvc1","HEVC VA-API uses hvc1 tag in MP4");
  var vbr=Commands.Vaapi(s,new Preset {Engine=Engine.Vaapi,RateMode=RateMode.VBR,Bitrate=5000},m,"in.ts","out.mkv");
  int bv=vbr.Args.IndexOf("-b:v");Check(bv>=0&&vbr.Args[bv+1]=="5000k"&&!vbr.Args.Contains("-qp"),"VA-API VBR maps to bitrate");
  var cbr=Commands.Vaapi(s,new Preset {Engine=Engine.Vaapi,RateMode=RateMode.CBR,Bitrate=5000},m,"in.ts","out.mkv");
  Check(cbr.Args.Contains("-maxrate")&&cbr.Args.Contains("-bufsize"),"VA-API CBR adds maxrate and bufsize");
  var custom=new Settings {Ffmpeg=s.Ffmpeg,VaapiDevice="/dev/dri/renderD129"};
  var overridden=Commands.Vaapi(custom,new Preset {Engine=Engine.Vaapi},m,"in.ts","out.mp4");
  int dev2=overridden.Args.IndexOf("-vaapi_device");Check(dev2>=0&&overridden.Args[dev2+1]=="/dev/dri/renderD129","VA-API device is overridable in settings");
  Reject(()=>Commands.Hardware(s,new Preset {Engine=Engine.Vaapi},m,"in.ts","out.mp4"),"rigaya builder rejects VA-API presets");
  if(!OperatingSystem.IsWindows()&&File.Exists(Vaapi.Device(s)))Check(Vaapi.Available(s),"VA-API encode available on this machine");
 }
}
