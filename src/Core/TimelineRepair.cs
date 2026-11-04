using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class TimelineRepairReport {
  public double OriginalSeconds,RepairedSeconds,RemovedSeconds;
  public string Method="FFmpeg MPEG-TS discontinuity correction / stream copy";
  public string Warning="欠落した内容は復元できません。破損境界の映像乱れ・音ずれ・字幕ずれは残る場合があります。";
 }
 public sealed class TimelineRepairReceipt {public string Source="",Output="";}
 public static class TimelineRepair {
  public static string CachedInput(Job j,Settings s,string data) {
   if(!j.Preset.RepairTimeline)return j.Input;
   string output=Path.Combine(EncodeCache.Root(data,s.EncodeCacheFolder),j.Id,"Work","timeline-repaired.ts");
   var receipt=Store.Load(output+".json",()=>new TimelineRepairReceipt());
   string key=Store.Fingerprint(j.Input)+"|repair-v2|"+j.Metadata.ServiceId+"|"+Store.Fingerprint(s.Ffmpeg);
   if(!File.Exists(output)||receipt.Source!=key||receipt.Output!=Store.Fingerprint(output))throw new InvalidDataException("補正済みTSのキャッシュがありません。ジョブを再試行してください。");
   return output;
  }
  public static async Task Prepare(Job j,Settings s,string data,CancellationToken token,Action<string> log,Action changed) {
   if(!j.Preset.RepairTimeline)return;
   var original=j.Metadata;string fingerprint=Store.Fingerprint(j.Input);
   if(original.ServiceId<=0)throw new InvalidDataException("時刻補正には対象サービスIDが必要です。");
   string folder=EncodeCache.WorkFolder(data,s,j),output=Path.Combine(folder,"timeline-repaired.ts"),receipt=output+".json";
   string key=fingerprint+"|repair-v2|"+original.ServiceId+"|"+Store.Fingerprint(s.Ffmpeg);
   var previous=Store.Load(receipt,()=>new TimelineRepairReceipt());
   if(!File.Exists(output)||previous.Source!=key||previous.Output!=Store.Fingerprint(output)) {
    j.Message="録画の時刻欠落を補正中（再エンコードなし）";changed();
    string pending=output+"."+Guid.NewGuid().ToString("N")+".tmp";
    try {
     var args=new List<string>{"-hide_banner","-nostdin","-loglevel","error","-y","-fflags","+genpts","-dts_delta_threshold","0.01","-i",j.Input,"-map","0:p:"+original.ServiceId,"-c","copy","-copy_unknown","-map_metadata","-1","-mpegts_service_id",original.ServiceId.ToString(),"-muxdelay","0"};
     if(original.NetworkId>0)args.AddRange(new[]{"-mpegts_original_network_id",original.NetworkId.ToString()});
     if(original.TransportId>0)args.AddRange(new[]{"-mpegts_transport_stream_id",original.TransportId.ToString()});
     if(!double.IsFinite(original.Fps)||original.Fps<=0)throw new InvalidDataException("時刻補正には有効なFPSが必要です。");
     // A reference picture may announce the post-gap PTS before its DTS jumps.
     // Correct only this extreme reordering outlier; complete frame validation
     // below still rejects overlaps and discontinuities after remuxing.
     args.AddRange(new[]{"-bsf:v","setts=pts='if(gt(abs(PTS-DTS)*TB,0.5),DTS+1/("+Commercials.N(original.Fps)+"*TB),PTS)'"});
     args.AddRange(new[]{"-f","mpegts",pending});
     var result=await Processes.Run(s.Ffmpeg,args,token,log);
     if(result.ExitCode!=0||!File.Exists(pending)||new FileInfo(pending).Length<188)throw new InvalidDataException("録画の時刻補正に失敗しました："+Converter.Tail(result.Error));
     token.ThrowIfCancellationRequested();if(Store.Fingerprint(j.Input)!=fingerprint)throw new IOException("時刻補正中に元TSが変更されました。");
     File.Move(pending,output,true);Store.Save(receipt,new TimelineRepairReceipt {Source=key,Output=Store.Fingerprint(output)});
    }finally{if(File.Exists(pending))File.Delete(pending);}
   }else log("検証対象の時刻補正TSをキャッシュから再利用します。");
   var m=await MetadataReader.Read(output,s,original.ServiceId,token,log);
   if(m.Width!=original.Width||m.Height!=original.Height||Math.Abs(m.Fps-original.Fps)>.001||m.AudioTracks.Count!=original.AudioTracks.Count||original.SubtitleIndex>=0&&(m.SubtitleIndex<0||m.SubtitleCodec!=original.SubtitleCodec))throw new InvalidDataException("時刻補正後の映像・音声・字幕構成が元TSと一致しません。");
   for(int i=0;i<m.AudioTracks.Count;i++)m.AudioTracks[i].SourceIndex=original.AudioTracks[i].Index;
   // Keep programme and naming information from the original EIT/sidecar.
   m.Title=original.Title;m.Channel=original.Channel;m.Description=original.Description;m.Episode=original.Episode;m.Source=original.Source;m.OriginalName=original.OriginalName;m.Start=original.Start;m.EventId=original.EventId;m.NetworkId=original.NetworkId;m.TransportId=original.TransportId;
   j.Message="補正後の全フレーム時刻を検証中";changed();
   string cm=Commercials.Folder(j,data);Directory.CreateDirectory(cm);
   string frames=Path.Combine(cm,"frame-times.csv"),identity=frames+".identity";
   string frameKey=Store.Fingerprint(output)+"|"+m.VideoIndex+"|"+Store.Fingerprint(s.Ffprobe)+"|streamed-frames-v2";
   if(!File.Exists(frames)||!File.Exists(identity)||File.ReadAllText(identity)!=frameKey) {
    string pending=frames+"."+Guid.NewGuid().ToString("N")+".tmp";
    try {
     var result=await Processes.RunToFile(s.Ffprobe,new[]{"-v","error","-threads","4","-select_streams",m.VideoIndex.ToString(),"-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",output},pending,token);
     if(result.ExitCode!=0)throw new InvalidDataException("補正後のフレーム時刻を取得できません："+Converter.Tail(result.Error));
     token.ThrowIfCancellationRequested();File.Move(pending,frames,true);File.WriteAllText(identity,frameKey);
    }finally{if(File.Exists(pending))File.Delete(pending);}
   }
   var times=CmAnalysis.ReadFrameTimes(frames,m.VideoStart);CmAnalysis.ValidateTimeline(times,times.Length,m);
   if(Store.Fingerprint(j.Input)!=fingerprint)throw new IOException("時刻検証中に元TSが変更されました。");
   j.WorkingInput=output;j.Metadata=m;
   j.TimelineRepair=new TimelineRepairReport {OriginalSeconds=original.Duration,RepairedSeconds=m.Duration,RemovedSeconds=Math.Max(0,original.Duration-m.Duration)};
   log($"時刻補正：{original.Duration:F3}秒 → {m.Duration:F3}秒（{j.TimelineRepair.RemovedSeconds:F3}秒短縮）。欠落映像は復元せず、残った映像・音声・字幕を使用します。元TSは保持します。");changed();
  }
 }
}
