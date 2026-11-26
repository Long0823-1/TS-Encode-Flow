using System;
using System.Diagnostics;
using System.IO;

namespace TsEncodeFlow {
 public static class Vaapi {
  public const string DefaultDevice="/dev/dri/renderD128";
  public static string Device(Settings s)=>string.IsNullOrWhiteSpace(s.VaapiDevice)?DefaultDevice:s.VaapiDevice.Trim();
  public static bool Supports(Codec codec)=>codec==Codec.H264||codec==Codec.HEVC;
  static string encoderPath="",encoderList="";
  // VA-APIエンコードがこのマシンで使えるか（Windowsでは常に不可）。ffmpegのエンコーダー一覧はパスごとに一度だけ調べる。
  public static bool Available(Settings s) {
   if(OperatingSystem.IsWindows())return false;
   try {
    if(string.IsNullOrEmpty(s.Ffmpeg)||!File.Exists(s.Ffmpeg)||!File.Exists(Device(s)))return false;
    if(encoderPath!=s.Ffmpeg) {
     var info=new ProcessStartInfo(s.Ffmpeg,"-hide_banner -encoders") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
     using var proc=Process.Start(info);var read=proc.StandardOutput.ReadToEndAsync();_ = proc.StandardError.ReadToEndAsync();
     if(!proc.WaitForExit(10000)){try{proc.Kill();}catch{return false;}}
     encoderList=read.GetAwaiter().GetResult();encoderPath=s.Ffmpeg;
    }
    return encoderList.Contains("h264_vaapi")&&encoderList.Contains("hevc_vaapi");
   }catch{return false;}
  }
 }
}
