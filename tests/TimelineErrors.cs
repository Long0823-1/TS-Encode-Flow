using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task LateTimelineClip(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--late-timeline-clip"));Directory.CreateDirectory(folder);
  var settings=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));var original=Store.Load<Job>(Path.GetFullPath(Option(args,"--job")),()=>throw new Exception("job missing"));
  string clip=Path.Combine(folder,"late-recording.ts");
  var copied=await Processes.Run(settings.Ffmpeg,new[]{"-v","error","-y","-ss","11000","-i",original.Input,"-t","12","-map","0:"+original.Metadata.VideoIndex,"-map","0:"+original.Metadata.AudioIndex,"-c","copy","-mpegts_service_id",original.Metadata.ServiceId.ToString(),"-f","mpegts",clip},CancellationToken.None);if(copied.ExitCode!=0)throw new Exception(copied.Error);
  var preset=Store.Clone(original.Preset);preset.CmMode=CmMode.Off;preset.KeepSubtitles=false;
  var job=new Job {Input=clip,Preset=preset,OutputFolder=Path.Combine(folder,"Output"),FileName="AMD-late-recording",LogFile=Path.Combine(folder,"Data","Logs","encode.log")};
  Directory.CreateDirectory(Path.GetDirectoryName(job.LogFile));await Converter.Convert(job,settings,CancellationToken.None,Console.WriteLine,()=>{});
  var decoded=await Processes.Run(settings.Ffmpeg,new[]{"-v","error","-xerror","-i",job.Output,"-map","0:v:0","-map","0:a:0","-f","null","-"},CancellationToken.None);if(decoded.ExitCode!=0)throw new Exception(decoded.Error);
  Store.Save(Path.Combine(folder,"job.json"),job);Console.WriteLine("LATE RECORDING AMD CONVERSION PASS "+job.Output);
 }
 static async Task TimelineFile(string[] args) {
  string input=Path.GetFullPath(Option(args,"--input")),folder=Path.GetFullPath(Option(args,"--timeline-file"));Directory.CreateDirectory(folder);
  var settings=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));
  var m=await MetadataReader.Read(input,settings,0,CancellationToken.None,Console.WriteLine);
  string file=Path.Combine(folder,"complete-frames.csv");
  if(!File.Exists(file)){var r=await Processes.RunToFile(settings.Ffprobe,new[]{"-v","error","-threads","4","-select_streams",m.VideoIndex.ToString(),"-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",input},file,CancellationToken.None);if(r.ExitCode!=0)throw new Exception(r.Error);}
  var times=CmAnalysis.ReadFrameTimes(file,m.VideoStart);Console.WriteLine("FRAME BYTES "+new FileInfo(file).Length+" / COUNT "+times.Length+" / END "+(times[^1]+1/m.Fps)+" / HEADER DURATION "+m.Duration);
  CmAnalysis.ValidateTimeline(times,times.Length,m);Store.Save(Path.Combine(folder,"metadata.json"),m);Console.WriteLine("TIMELINE PASS "+m.Duration);
 }
}
