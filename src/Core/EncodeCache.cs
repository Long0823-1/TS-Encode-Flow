using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TsEncodeFlow {
 public static class EncodeCache {
  public static void ValidateFolder(string folder) {
   if(string.IsNullOrWhiteSpace(folder))return;
   if(!Path.IsPathFullyQualified(folder))throw new ArgumentException("キャッシュの保存先は絶対パスで指定してください。空欄なら標準の保存先です。");
   string path=Path.GetFullPath(folder);if(File.Exists(path))throw new ArgumentException("キャッシュの保存先にファイルは指定できません。");
  }
  public static string Root(string data,string folder="") {
   ValidateFolder(folder);return string.IsNullOrWhiteSpace(folder)?Path.Combine(Path.GetFullPath(data),"EncodeCache"):Path.Combine(Path.GetFullPath(folder),"TS-Encode-Flow-Cache","EncodeCache");
  }
  public static string Register(string data,Settings settings,Job job) {
   if(!Guid.TryParseExact(job.Id,"N",out _))throw new ArgumentException("キャッシュ対象のジョブIDが不正です。");
   job.EncodeCacheFolders??=new();string selected=settings.EncodeCacheFolder?.Trim()??"";if(!job.EncodeCacheFolders.Contains(selected,StringComparer.OrdinalIgnoreCase))job.EncodeCacheFolders.Add(selected);return Root(data,selected);
  }
  public static string WorkFolder(string data,Settings settings,Job job) {
   if(!Guid.TryParseExact(job.Id,"N",out _))throw new ArgumentException("キャッシュ対象のジョブIDが不正です。");
   string folder=Path.Combine(Register(data,settings,job),job.Id,"Work");Directory.CreateDirectory(folder);return folder;
  }
  public static void ClearForJob(string data,Job job) {
   foreach(string folder in (job.EncodeCacheFolders??new()).Append("").Distinct(StringComparer.OrdinalIgnoreCase))Clear(data,job.Id,folder);
  }
  public static void Clear(string data,string id,string folder="") {
   if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("キャッシュ対象のジョブIDが不正です。");
   string scope=Root(data,folder),root=Path.GetFullPath(Path.Combine(scope,id));if(!root.StartsWith(scope.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("キャッシュの削除範囲が不正です。");if(!Directory.Exists(root))return;
   for(var parent=new DirectoryInfo(root);parent!=null;parent=parent.Parent)if((parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("リンクされた場所のキャッシュは削除しません。");
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
