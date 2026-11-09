using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TsEncodeFlow;

static partial class Tests {
 static void DelogoTests(string root) {
  // Script generation: private runtime plugins, L-SMASH source with explicit stream, DelogoHD, explicit FPS.
  string script=Delogo.Script(@"C:\tools\LSMASHSource.dll",@"C:\tools\DelogoHD.dll",@"C:\work\delogo-index.lwi",2,@"C:\data\Logos\1-2-3-1920x1080-v0001.lgd",30000.0/1001);
  Check(script.Contains("ClearAutoloadDirs()"),"delogo script clears autoload dirs");
  Check(script.Contains("LoadPlugin(\"C:/tools/LSMASHSource.dll\")")&&script.Contains("LoadPlugin(\"C:/tools/DelogoHD.dll\")"),"delogo script loads LSMASHSource and DelogoHD explicitly");
  Check(script.Contains("LWLibavVideoSource(\"C:/work/delogo-index.lwi\",stream_index=2,threads=4,repeat=false)"),"delogo script opens the mapped L-SMASH stream of the private index");
  Check(script.Contains("v=v.DelogoHD(logofile=\"C:/data/Logos/1-2-3-1920x1080-v0001.lgd\")"),"delogo script applies DelogoHD with the station LGD");
  Check(script.Contains("v.AssumeFPS(30000,1001)"),"delogo script pins CFR fps as a reduced rational (y4m header stays small)");
  Check(Delogo.FpsRational(30000.0/1001)==(30000,1001)&&Delogo.FpsRational(24000.0/1001)==(24000,1001)&&Delogo.FpsRational(60000.0/1001)==(60000,1001)&&Delogo.FpsRational(30)==(30,1)&&Delogo.FpsRational(25)==(25,1),"NTSC and integer frame rates map to exact rationals (29.97 must not become 2997/100)");
  Check(Commands.SarRatio(4.0/3)=="4:3"&&Commands.SarRatio(32.0/27)=="32:27","SAR maps to an exact ratio");
  var sarCmd=Commands.Hardware(new Settings(),new Preset {Engine=Engine.VCEEncC,Deinterlace=Deinterlace.Yadif},new Metadata {Width=1440,Height=1080,Sar=4.0/3,FieldOrder="tt"},"in.mkv","out.mkv");
  Check(sarCmd.Args.Contains("--sar")&&sarCmd.Args[sarCmd.Args.IndexOf("--sar")+1]=="4:3"&&sarCmd.Args.Contains("--vpp-yadif"),"hardware encode keeps the 16:9 SAR explicitly when the resolution is unchanged");
  Reject(()=>Delogo.Script("bad\"path.dll","x.dll","s",-1,"l",30),"delogo script rejects quote in path");
  // Segment scripts chain one DelogoHD call per detected display segment, with fades.
  string segmented=Delogo.Script("a.dll","b.dll","idx.lwi",-1,"logo.lgd",30000.0/1001,new List<LogoSegment>{new LogoSegment{Start=100,End=200,FadeIn=15,FadeOut=0},new LogoSegment{Start=500,End=900,FadeIn=0,FadeOut=30}});
  Check(segmented.Contains("v=v.DelogoHD(logofile=")&&segmented.Contains(",start=100,end=200,fadein=15)")&&segmented.Contains(",start=500,end=900,fadeout=30)"),"delogo script chains per-segment DelogoHD calls with fades");
  Check(!segmented.Contains("stream_index"),"delogo script omits stream_index when unmapped");
  // CM cut scripts trim the kept programme ranges inside AVS.
  string cut=Delogo.Script("a.dll","b.dll","idx.lwi",0,"logo.lgd",30000.0/1001,null,new List<(int,int)>{(0,299),(600,899)});
  Check(cut.Contains("v=v.Trim(0,299)++v.Trim(600,899)"),"delogo script trims CM keep ranges in AVS");
  // Trim decision: the intermediate is pre-cut only when the plain pipeline consumes it.
  // The advanced pipeline seeks original-timeline positions, so it must receive the full-length intermediate.
  var trimMeta=new Metadata {Fps=25,Duration=36};
  var plainJob=new Job {Metadata=trimMeta,Preset=new Preset {RemoveLogo=true,CmMode=CmMode.Cut},Commercials=Commercials.Build(new[]{new TimeRange {Start=10,End=20}},36,25,"test")};
  var trim=Delogo.TrimFrames(plainJob);
  Check(trim!=null&&trim.Count==2&&trim[0]==(0,249)&&trim[1]==(500,899),"delogo pre-trims CM keep ranges for the plain pipeline");
  var advancedJob=new Job {Metadata=trimMeta,Preset=new Preset {RemoveLogo=true,CmMode=CmMode.Cut,BitDepth=10},Commercials=plainJob.Commercials};
  Check(Delogo.TrimFrames(advancedJob)==null,"delogo keeps the full-length intermediate for the advanced pipeline");
  var chaptersJob=new Job {Metadata=trimMeta,Preset=new Preset {RemoveLogo=true,CmMode=CmMode.Chapters},Commercials=plainJob.Commercials};
  Check(Delogo.TrimFrames(chaptersJob)==null,"delogo does not trim for CM chapters mode");
  var noCmJob=new Job {Metadata=trimMeta,Preset=new Preset {RemoveLogo=true}};
  Check(Delogo.TrimFrames(noCmJob)==null,"delogo does not trim without a CM plan");
  // Frame range conversion follows the CM convention: half-second boundaries round to even frames
  // (Math.Round defaults), so 20.5s at 25fps starts at frame 512, not 513.
  var frames=Delogo.FrameRanges(new List<TimeRange>{new TimeRange{Start=0,End=10},new TimeRange{Start=20.5,End=30.25}},25);
  Check(frames.Count==2&&frames[0]==(0,249)&&frames[1]==(512,755),"delogo frame ranges convert seconds to inclusive frames");
  Check(Delogo.FrameRanges(null,25).Count==0,"delogo frame ranges handle no CM plan");
  // logoframe -oa parsing: S/E pairs, fade seconds become fade frames, unpaired/garbled lines are dropped.
  var segs=Delogo.ParseLogoSegments("   100 S 1 ALL    100    112\n   300 E 2 ALL    292    300\n   500 S 0 ALL    500    500\n   900 E 0 ALL    900    900\ngarbage line\n   950 E 0 ALL    950    950\n",25);
  Check(segs.Count==2&&segs[0].Start==100&&segs[0].End==300&&segs[0].FadeIn==25&&segs[0].FadeOut==50&&segs[1].Start==500&&segs[1].End==900&&segs[1].FadeIn==0,"delogo parses logoframe segments and converts fades");
  var mergedSegs=Delogo.ParseLogoSegments("   100 S 1 ALL    100    100\n   200 E 1 ALL    200    200\n   201 S 1 ALL    201    201\n   400 E 1 ALL    400    400\n",25);
  Check(mergedSegs.Count==1&&mergedSegs[0].Start==100&&mergedSegs[0].End==400&&mergedSegs[0].FadeIn==0&&mergedSegs[0].FadeOut==0,"delogo merges adjacent segments without fades");
  Check(Delogo.ParseLogoSegments("",30).Count==0,"delogo parses empty logo output as no segments");
  // Logo resolution: nothing registered and no manual LGD is a clear, immediate error.
  string data=Path.Combine(root,"delogo-data");Directory.CreateDirectory(data);
  var job=new Job {Input=Path.Combine(root,"delogo-source.ts"),Metadata=new Metadata {NetworkId=1,TransportId=2,ServiceId=3,Channel="テスト局",Width=1920,Height=1080},Preset=new Preset {RemoveLogo=true}};
  bool missing=false;try{Delogo.ResolveLogo(data,job,new Settings());}catch(InvalidDataException){missing=true;}Check(missing,"delogo without a registered logo fails with guidance");
  bool missingFile=false;try{Delogo.ResolveLogo(data,job,new Settings {CmLogo=Path.Combine(root,"no-such.lgd")});}catch(InvalidDataException){missingFile=true;}Check(missingFile,"delogo with a missing manual LGD fails with guidance");
  // VideoOnly clone: intermediate is stream 0 with no audio, original untouched.
  var original=new Metadata {VideoIndex=3,AudioIndex=4,Width=1440,Height=1080,Sar=4.0/3,Title="番組"};
  var clone=Delogo.VideoOnly(original);
  Check(clone.VideoIndex==0&&clone.AudioIndex==-1&&clone.Width==1440&&clone.Sar==4.0/3&&clone.Title=="番組","delogo video-only metadata remaps streams and keeps geometry");
  Check(original.VideoIndex==3&&original.AudioIndex==4,"delogo video-only clone leaves the original metadata untouched");
  // Tool packages resolve uniquely against the real release asset lists.
  var delogohd=ToolInstaller.Packages.Single(p=>p.Id=="delogohd");
  var hdAssets=new[]{"delogohd-windows-arm64.zip","delogohd-windows-x64.zip","dualsynth-delogohd_13.0.0+ubuntu24.04_amd64.deb","dualsynth-delogohd_13.0.0+ubuntu24.04_arm64.deb","dualsynth-delogohd_13.0.0+ubuntu26.04_amd64.deb","dualsynth-delogohd_13.0.0+ubuntu26.04_arm64.deb","libDelogoHD.dylib.zst"};
  Check(hdAssets.Count(a=>Regex.IsMatch(a,delogohd.Pattern))==1&&Regex.IsMatch("delogohd-windows-x64.zip",delogohd.Pattern),"delogohd package pattern selects exactly the windows x64 zip");
  Check(delogohd.Executable=="DelogoHD.dll"&&delogohd.Setting=="DelogoPlugin","delogohd package targets DelogoHD.dll and the DelogoPlugin setting");
  foreach(var package in ToolInstaller.Packages)Check(typeof(Settings).GetProperty(package.Setting)!=null,"tool package setting exists: "+package.Id);
  // Preset persistence: off by default, survives the JSON roundtrip used for queues/settings.
  Check(new Preset().RemoveLogo==false,"delogo is off by default");
  Check(Store.Clone(new Preset {RemoveLogo=true}).RemoveLogo,"delogo flag survives preset clone roundtrip");
 }
}
