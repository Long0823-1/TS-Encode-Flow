using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task UserFile(string[] args) {
  string input=Option(args,"--user-file"),mode=Option(args,"--mode"),folder=Path.Combine(Option(args,"--out"),mode);Directory.CreateDirectory(folder);
  var settings=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe"),Vceenc=Option(args,"--vce"),CaptionTool=Option(args,"--caption"),OutputFolder=folder};
  if(args.Contains("--dll"))settings.EpgDll=Option(args,"--dll");
  var preset=new Preset {Name=mode,Engine=Engine.VCEEncC,Codec=mode.Contains("hevc")?Codec.HEVC:Codec.H264,Container=mode.Contains("hevc")?Container.mkv:Container.mp4,Quality=mode.Contains("hevc")?24:23,Speed="balanced",Width=mode.Contains("hevc")?0:1280,Height=mode.Contains("hevc")?0:720,Deinterlace=mode.Contains("hevc")?Deinterlace.Bwdif:Deinterlace.Yadif,CopyAudio=mode.Contains("hevc")};
  var j=new Job {Input=input,Preset=preset,OutputFolder=folder,FileName="{OriginalName}",State=State.Running,LogFile=Path.Combine(folder,"Data","Logs","encode.log")};Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));
  DateTime next=DateTime.MinValue;object gate=new object();Action<string> log=line=>{lock(gate){File.AppendAllText(j.LogFile,line+Environment.NewLine);if(DateTime.UtcNow>next){Console.WriteLine(DateTime.Now.ToString("HH:mm:ss")+" "+mode+" "+j.Message+" "+j.Progress.ToString("0.0")+"%");next=DateTime.UtcNow.AddSeconds(20);}}};
  try {
   await Converter.Convert(j,settings,CancellationToken.None,log,()=>Store.Save(Path.Combine(folder,"progress.json"),j));j.State=State.Completed;Store.Save(Path.Combine(folder,"progress.json"),j);
   var result=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",j.Output},CancellationToken.None,null,60);File.WriteAllText(Path.Combine(folder,"ffprobe.json"),result.Output);
   Check(result.ExitCode==0,"whole user TS "+mode+" output readable");
   var data=Store.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(result.Output);var streams=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).ToList();var video=streams.First(s=>MetadataReader.Text(s,"codec_type")=="video");
   Check(MetadataReader.Text(video,"codec_name")== (preset.Codec==Codec.HEVC?"hevc":"h264"),"user TS codec verified "+mode);
   Check(streams.Any(s=>MetadataReader.Text(s,"codec_type")=="audio"),"user TS audio verified "+mode);
   double duration=MetadataReader.Number(MetadataReader.Text(MetadataReader.Obj(data["format"]),"duration"));Check(Math.Abs(duration-j.Metadata.Duration)<3,"whole recording duration preserved "+mode);
   var tags=MetadataReader.Obj(MetadataReader.Obj(data["format"])["tags"]);Check(tags.Any(x=>x.Key.Equals("title",StringComparison.OrdinalIgnoreCase)&&x.Value.ToString()==j.Metadata.Title),"user program title metadata verified "+mode);
   Console.WriteLine("OUTPUT "+j.Output+" duration="+duration+" subtitles="+j.Metadata.SubtitleStatus);
  }catch(Exception e){j.State=State.Failed;j.Message=e.ToString();Store.Save(Path.Combine(folder,"progress.json"),j);throw;}
 }
}
