using System;
using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public enum CadenceMode { Preset, MixedVfr }
 public enum VideoRange { Preserve, Limited, Full }
 public enum VideoMatrix { Preserve, Bt709, Smpte170m, Bt2020nc }
 public enum DitherMode { None, Bayer, ErrorDiffusion }
 public sealed class AudioTrack {
  public int Index,Channels,Pid;
  public int SourceIndex=-1;
  public string Codec="",Language="und",Title="",Layout="";
  public bool Default,Commentary;
  public double Start;
 }
 public sealed class EncodePart {
  public double Start,End,OutputStart;
  public string Mode="preset",File="",Hash="";
  public int Index;
  public bool Complete,Reused;
 }
 public sealed class AdvancedRun {
  public string CacheFolder="",Key="";
  public int ReusedParts;
  public List<EncodePart> Parts=new();
 }
 public sealed class SignalIssue {
  public double Seconds;
  public string Kind="",Detail="";
  public long Offset;
  public bool Approximate;
 }
 public sealed class SignalReport {
  public string Fingerprint="";
  public int VideoIndex=-1;
  public string AudioIndices="";
  public long Packets,ContinuityErrors,TransportErrors,SyncErrors;
  public int TimestampGaps;
  public bool Truncated;
  public List<SignalIssue> Issues=new();
 }
 public sealed class QualityTrial {
  public int Quality;
  public double Seconds,EncodedSeconds,Megabytes,EstimatedMegabytes,Speed,Ssim;
  public string File="",Metric="";
 }
 public static class AdvancedOptions {
  public static bool Enabled(Preset p)=>p.Cadence==CadenceMode.MixedVfr||p.PipeGpu||p.ResumeChunks||p.AllAudio||!string.IsNullOrWhiteSpace(p.AudioStreams)||p.BitDepth!=8||p.ColorMatrix!=VideoMatrix.Preserve||p.InputColorMatrix!=VideoMatrix.Preserve||p.ColorRange!=VideoRange.Preserve||p.Dither!=DitherMode.ErrorDiffusion;
  public static List<AudioTrack> Audio(Preset p,Metadata m) {
   var all=m.AudioTracks??new();if(all.Count==0&&m.AudioIndex>=0)all=new(){new(){Index=m.AudioIndex,Start=m.AudioStart}};
   if(!string.IsNullOrWhiteSpace(p.AudioStreams)) {
    var ids=p.AudioStreams.Split(',').Select(s=>int.TryParse(s.Trim(),out int i)?i:throw new ArgumentException("音声番号はカンマ区切りの整数で指定してください。")).ToList();
    if(ids.Distinct().Count()!=ids.Count||ids.Any(i=>!all.Any(t=>(t.SourceIndex>=0?t.SourceIndex:t.Index)==i)))throw new ArgumentException("選択サービスに存在しない、または重複した音声番号です。");
    return ids.Select(i=>all.First(t=>(t.SourceIndex>=0?t.SourceIndex:t.Index)==i)).ToList();
   }return p.AllAudio?all:all.Take(1).ToList();
  }
  public static void Validate(Preset p) {
   if(!Enum.IsDefined(p.Cadence)||!Enum.IsDefined(p.ColorMatrix)||!Enum.IsDefined(p.InputColorMatrix)||!Enum.IsDefined(p.ColorRange)||!Enum.IsDefined(p.Dither))throw new ArgumentException("詳細映像設定が不正です。");
   if(p.BitDepth!=8&&p.BitDepth!=10)throw new ArgumentException("ビット深度は8または10です。");
   if(p.BitDepth==10&&p.Engine!=Engine.CPU&&p.Codec==Codec.H264)throw new ArgumentException("GPUの10bit出力はHEVCまたはAV1を選んでください。");
   if(p.ChunkSeconds<10||p.ChunkSeconds>600)throw new ArgumentException("再開用区間は10～600秒です。");
   if(p.ColorMatrix!=VideoMatrix.Preserve&&p.Dither==DitherMode.Bayer)throw new ArgumentException("色域変換時のディザリングは「なし」または「誤差拡散」を選んでください。");
   if(p.Cadence==CadenceMode.MixedVfr&&!string.IsNullOrWhiteSpace(p.Fps))throw new ArgumentException("混在VFRではFPS指定を空欄にしてください。");
   if(Enabled(p)&&!string.IsNullOrWhiteSpace(p.ExtraArgs))throw new ArgumentException("詳細パイプラインでは追加引数を空欄にしてください。画質・色・FPSは専用項目で指定します。");
  }
 }
}
