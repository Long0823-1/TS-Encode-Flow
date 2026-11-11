using System;
using System.IO;
namespace TsEncodeFlow {
 public static class StorageSpace {
  public static string Failure(string error,int exitCode,string destination) {
   error??="";
   if(exitCode==-28||error.IndexOf("No space left on device",StringComparison.OrdinalIgnoreCase)>=0||error.IndexOf("disk full",StringComparison.OrdinalIgnoreCase)>=0) {
    string drive=Path.GetPathRoot(Path.GetFullPath(destination));
    return "保存先の空き容量が不足しています（"+drive+"）。キャッシュと出力先の空きを確保してから再試行してください。元TSは保持しています。";
   }
   return Converter.Tail(error);
  }
  public static void Require(string destination,long bytes=268435456) {
   string root=Path.GetPathRoot(Path.GetFullPath(destination));
   if(string.IsNullOrEmpty(root)||root.StartsWith(@"\\"))return;
   var drive=new DriveInfo(root);
   if(drive.IsReady&&drive.AvailableFreeSpace<bytes)throw new IOException("保存先の空き容量が不足しています（"+root+"、空き "+(drive.AvailableFreeSpace/1048576)+" MB）。キャッシュと出力先の空きを確保してから再試行してください。元TSは保持しています。");
  }
 }
}
