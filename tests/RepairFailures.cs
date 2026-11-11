using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static async Task RepairFailures(string[] args) {
  string data=Path.GetFullPath(Option(args,"--repair-failures")),original=Option(args,"--tool-data");Directory.CreateDirectory(data);
  var config=Integration.LoadSettings(original);config.EncodeCacheFolder=Path.Combine(Path.GetPathRoot(config.OutputFolder),"TS-Encode-Flow-QA-v0616");
  var jobs=Store.Load<System.Collections.Generic.List<Job>>(Option(args,"--queue-snapshot"),()=>throw new Exception("queue snapshot missing"));
  int count=0;
  foreach(var source in jobs.Where(j=>j.State==State.Failed&&j.Message.Contains("時刻補正に失敗")&&File.Exists(j.Input))) {
   Console.WriteLine("REPAIR CHECK "+Path.GetFileName(source.Input));string before=Store.Fingerprint(source.Input);
   var job=new Job {Input=source.Input,Preset=Store.Clone(source.Preset)};job.Metadata=await MetadataReader.Read(job.Input,config,job.Preset.ServiceId,CancellationToken.None,Console.WriteLine);
   try{await TimelineRepair.Prepare(job,config,data,CancellationToken.None,Console.WriteLine,()=>{});}
   catch(InvalidDataException e)when(e.Message.Contains("時刻が重複")||e.Message.Contains("補正後も")) {
    Console.WriteLine("SOURCE TIMELINE REJECTED (not an encoding success): "+Path.GetFileName(source.Input)+" / "+e.Message);
    Check(Store.Fingerprint(source.Input)==before,"unrepairable timeline leaves the original recording unchanged");continue;
   }
   Check(job.Metadata.Duration>0&&job.Metadata.AudioIndex>=0&&File.Exists(job.MediaInput),"reported disk-full recording now remuxes and passes complete frame verification");
   string clip=Path.Combine(config.EncodeCacheFolder,"clip-"+job.Id+".ts");
   var cut=await Processes.Run(config.Ffmpeg,new[]{"-hide_banner","-nostdin","-v","error","-y","-ss","30","-i",job.MediaInput,"-t","8","-map","0:v:0","-map","0:a:0","-c","copy","-mpegts_service_id",job.Metadata.ServiceId.ToString(),"-f","mpegts",clip},CancellationToken.None);
   Check(cut.ExitCode==0,"repaired recording prepares AMD test interval");
   var preset=Store.Clone(source.Preset);preset.RepairTimeline=false;preset.CmMode=CmMode.Off;preset.KeepSubtitles=false;preset.RemoveLogo=false;preset.Engine=Engine.VCEEncC;preset.PipeGpu=false;preset.ResumeChunks=false;preset.AutoInverseTelecine=false;preset.Cadence=CadenceMode.Preset;
   string logo=StationLogos.Find(original,job);config.CmLogo=logo??"";
   // One actual logo-erasure conversion also exercises the copied runtime DLLs
   // and automatic readonly-cache cleanup after the encoder releases them.
   if(count==0&&logo!=null)preset.RemoveLogo=true;
   var test=new Job {Input=clip,Preset=preset,OutputFolder=Path.Combine(config.EncodeCacheFolder,"Outputs"),FileName="AMD-test-"+job.Id,LogFile=Path.Combine(data,"Logs",job.Id+".log")};Directory.CreateDirectory(Path.GetDirectoryName(test.LogFile));
   await Converter.Convert(test,config,CancellationToken.None,line=>File.AppendAllText(test.LogFile,line+Environment.NewLine),()=>{});
   var decode=await Processes.Run(config.Ffmpeg,new[]{"-v","error","-xerror","-i",test.Output,"-map","0:v:0","-map","0:a:0","-f","null","-"},CancellationToken.None);
   Check(decode.ExitCode==0,"actual AMD output from previously failing recording fully decodes");
   string testData=Path.GetDirectoryName(Path.GetDirectoryName(test.LogFile));Check(!Directory.Exists(Path.Combine(EncodeCache.Root(testData,config.EncodeCacheFolder),test.Id)),"successful actual AMD conversion removes its managed cache including readonly runtime");
   Check(Store.Fingerprint(source.Input)==before,"reported source recording remains unchanged");
   EncodeCache.Clear(data,job.Id,config.EncodeCacheFolder);File.Delete(clip);count++;
  }
  Check(count>0,"at least one reported repair failure was tested");
 }
}
