using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TsEncodeFlow {
 public static class EncodeCache {
  public static void Clear(string data,string id) {
   if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("キャッシュ対象のジョブIDが不正です。");
   string root=Path.Combine(Path.GetFullPath(data),"EncodeCache",id);if(!Directory.Exists(root))return;
   var dirs=new List<string>{root};for(int i=0;i<dirs.Count;i++) {
    if((File.GetAttributes(dirs[i])&FileAttributes.ReparsePoint)!=0)throw new IOException("リンクされたキャッシュフォルダーは削除しません。");
    foreach(string sub in Directory.EnumerateDirectories(dirs[i]))dirs.Add(sub);
   }
   // Validate the entire tree before any removal. Only this job's managed cache
   // is removed; output files and source TS paths are never taken from manifests.
   foreach(string dir in dirs.AsEnumerable().Reverse()){foreach(string file in Directory.EnumerateFiles(dir))File.Delete(file);Directory.Delete(dir);}
  }
 }
}
