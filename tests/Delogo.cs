using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TsEncodeFlow;

static partial class Tests {
 static void DelogoTests(string root) {
  // Script generation: private runtime plugins, L-SMASH source, DelogoHD, explicit FPS.
  string script=Delogo.Script(@"C:\tools\LSMASHSource.dll",@"C:\tools\DelogoHD.dll",@"C:\work\delogo-index.lwi",@"C:\data\Logos\1-2-3-1920x1080-v0001.lgd",30000.0/1001);
  Check(script.Contains("ClearAutoloadDirs()"),"delogo script clears autoload dirs");
  Check(script.Contains("LoadPlugin(\"C:/tools/LSMASHSource.dll\")")&&script.Contains("LoadPlugin(\"C:/tools/DelogoHD.dll\")"),"delogo script loads LSMASHSource and DelogoHD explicitly");
  Check(script.Contains("LWLibavVideoSource(\"C:/work/delogo-index.lwi\",threads=4,repeat=false)"),"delogo script opens the private L-SMASH index");
  Check(script.Contains(".DelogoHD(logofile=\"C:/data/Logos/1-2-3-1920x1080-v0001.lgd\")"),"delogo script applies DelogoHD with the station LGD");
  Check(script.Contains(".AssumeFPS(29970030,1000000)"),"delogo script pins CFR fps like CM analysis");
  Reject(()=>Delogo.Script("bad\"path.dll","x.dll","s","l",30),"delogo script rejects quote in path");
  // avs2pipemod pipe mode follows field order and carries SAR for anamorphic sources.
  Check(Delogo.PipeMode(new Metadata {FieldOrder="tt",Sar=4.0/3})=="-y4mt=4:3","delogo pipe mode tff with 4:3 SAR");
  Check(Delogo.PipeMode(new Metadata {FieldOrder="bb",Sar=1})=="-y4mb=1:1","delogo pipe mode bff");
  Check(Delogo.PipeMode(new Metadata {FieldOrder="progressive",Sar=32.0/27})=="-y4mp=32:27","delogo pipe mode progressive with SAR");
  Check(Delogo.PipeMode(new Metadata {FieldOrder="",Sar=0}).StartsWith("-y4mt="),"delogo pipe mode defaults to tff");
  // Combination guards fail fast instead of silently producing wrong output.
  Delogo.ValidateCombination(new Preset {RemoveLogo=true});Check(true,"delogo alone is a valid combination");
  Delogo.ValidateCombination(new Preset {CmMode=CmMode.Cut});Check(true,"delogo guard ignores presets without RemoveLogo");
  Reject(()=>Delogo.ValidateCombination(new Preset {RemoveLogo=true,CmMode=CmMode.Chapters}),"delogo rejects CM chapters combination");
  Reject(()=>Delogo.ValidateCombination(new Preset {RemoveLogo=true,CmMode=CmMode.Cut}),"delogo rejects CM cut combination");
  Reject(()=>Delogo.ValidateCombination(new Preset {RemoveLogo=true,Fps="30000/1001"}),"delogo rejects fps combination");
  Reject(()=>Delogo.ValidateCombination(new Preset {RemoveLogo=true,BitDepth=10}),"delogo rejects advanced pipeline combination");
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
  var pipe=ToolInstaller.Packages.Single(p=>p.Id=="avs2pipemod");
  Check(Regex.IsMatch("avs2pipemod-1.3.1.zip",pipe.Pattern)&&pipe.Executable=="avs2pipemod64.exe"&&pipe.Setting=="Avs2PipeMod","avs2pipemod package selects its zip and 64-bit exe");
  foreach(var package in ToolInstaller.Packages)Check(typeof(Settings).GetProperty(package.Setting)!=null,"tool package setting exists: "+package.Id);
  // Preset persistence: off by default, survives the JSON roundtrip used for queues/settings.
  Check(new Preset().RemoveLogo==false,"delogo is off by default");
  Check(Store.Clone(new Preset {RemoveLogo=true}).RemoveLogo,"delogo flag survives preset clone roundtrip");
 }
}
