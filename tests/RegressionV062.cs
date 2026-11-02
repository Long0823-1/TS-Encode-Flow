using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static void AudioSelectionV062() {
  var valid=new Dictionary<string,object>{{"index",2},{"id","0x112"},{"codec_type","audio"},{"codec_name","aac"},{"channels",2},{"sample_rate","48000"}};
  var empty=new Dictionary<string,object>(valid){{"start_time","0"}};empty["index"]=5;empty["channels"]=0;empty.Remove("sample_rate");int warnings=0;
  var selected=MetadataReader.DecodableAudio(new[]{valid,empty},_=>warnings++);Check(selected.Count==1&&MetadataReader.Int(selected[0],"index")==2&&warnings==1,"uninitialized advertised audio is excluded with a warning while valid audio is preserved");
  Check(MetadataReader.Pid(valid)==0x112,"audio PID parsed for stable TS mapping independently of probe index");
  bool stopped=false;try{MetadataReader.DecodableAudio(new[]{empty},_=>{});}catch(InvalidDataException){stopped=true;}Check(stopped,"an input with only invalid audio stops instead of silently producing a mute video");
 }
 static async Task RealV062(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--out"));Directory.CreateDirectory(folder);var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));var j=Store.Load<Job>(Path.GetFullPath(Option(args,"--new-error-job")),()=>new());string fingerprint=Store.Fingerprint(j.Input);object gate=new();Action<string> log=line=>{lock(gate)File.AppendAllText(Path.Combine(folder,"run.log"),line+"\n");};
  string output=Path.Combine(folder,"verified.mkv");
  if(args.Contains("--mux-existing")) {
   var metadata=await MetadataReader.Read(j.Input,s,j.Metadata.ServiceId,CancellationToken.None,log);Check(metadata.AudioTracks.All(t=>t.Channels>0)&&metadata.AudioTracks.Count>0,"actual recording selects initialized audio tracks only");j.Metadata.AudioTracks=metadata.AudioTracks;j.Metadata.AudioIndex=metadata.AudioIndex;
   foreach(var part in j.Advanced.Parts) {part.File=Path.Combine(folder,Path.GetFileName(part.File));Check(File.Exists(part.File)&&AdvancedEncoder.Hash(part.File)==part.Hash,"private cached part matches verified source hash");}
   await AdvancedEncoder.Mux(j,s,j.Advanced,folder,output,CancellationToken.None,log);await AdvancedEncoder.ValidateVideo(s,output,Commercials.Duration(j),CancellationToken.None);
   var p=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","a","-show_streams","-of","json",output},CancellationToken.None);var d=Store.Json.Deserialize<Dictionary<string,object>>(p.Output);Check(MetadataReader.Items(d,"streams").Count()==AdvancedOptions.Audio(j.Preset,j.Metadata).Count,"actual complete mux retains every selected valid audio track");
  }else {
   var part=j.Advanced.Parts.Last();Check(Math.Abs(part.End-j.Metadata.Duration)<0.01,"actual failure is final source interval");await AdvancedEncoder.EncodeVideo(j,s,part,output,CancellationToken.None,log);await AdvancedEncoder.ValidateVideo(s,output,part.End-part.Start,CancellationToken.None,true);
  }
  var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",output,"-map","0:v","-map","0:a?","-f","null","-"},CancellationToken.None,null,300);Check(decode.ExitCode==0,"actual repaired output fully decodes");Check(Store.Fingerprint(j.Input)==fingerprint,"original recording remains unchanged");Console.WriteLine("VERIFIED "+output);
 }
}
