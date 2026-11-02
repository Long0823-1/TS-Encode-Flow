using System;
using System.IO;
using TsEncodeFlow;
static partial class Tests {
 static void CacheLocationTests(string root) {
  string data=Path.Combine(root,"CacheSettingsData"),custom=Path.Combine(root,"SelectedDisk"),second=Path.Combine(root,"OtherDisk");
  Check(EncodeCache.Root(data)==Path.Combine(data,"EncodeCache"),"empty cache setting retains legacy default location");
  Check(EncodeCache.Root(data,custom)==Path.Combine(custom,"TS-Encode-Flow-Cache","EncodeCache"),"custom cache is isolated inside application-owned namespace");
  Reject(()=>EncodeCache.ValidateFolder("relative/cache"),"relative cache destinations rejected");
  Directory.CreateDirectory(custom);string original=Path.Combine(custom,"recording.ts");File.WriteAllText(original,"source");Reject(()=>EncodeCache.ValidateFolder(original),"file cannot be selected as cache directory");
  var settings=new Settings {EncodeCacheFolder=custom};settings.Validate();var copy=Store.Clone(settings);Check(copy.EncodeCacheFolder==custom,"cache setting survives settings serialization");
  var j=new Job {EncodeCacheFolders=new(){custom,second}};string neighbor=Path.Combine(EncodeCache.Root(data,custom),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(neighbor);File.WriteAllText(Path.Combine(neighbor,"keep.mkv"),"other job");
  foreach(string location in new[]{custom,second,""}){string folder=Path.Combine(EncodeCache.Root(data,location),j.Id,"key");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"part.mkv"),"cache");}
  var persisted=Store.Clone(j);EncodeCache.ClearForJob(data,persisted);Check(File.Exists(original)&&Directory.Exists(neighbor),"cleanup across old cache locations preserves source and other jobs");
  var legacyJob=new Job();string work=EncodeCache.WorkFolder(data,settings,legacyJob);Check(work.StartsWith(EncodeCache.Root(data,custom)+Path.DirectorySeparatorChar)&&Directory.Exists(work)&&legacyJob.EncodeCacheFolders.Contains(custom),"legacy GPU preprocessing uses the selected cache and records its ownership");EncodeCache.ClearForJob(data,legacyJob);Check(!Directory.Exists(work),"legacy GPU working directory is also removed by successful-job cleanup");
  Check(!Directory.Exists(Path.Combine(EncodeCache.Root(data,custom),j.Id))&&!Directory.Exists(Path.Combine(EncodeCache.Root(data,second),j.Id))&&!Directory.Exists(Path.Combine(EncodeCache.Root(data),j.Id)),"cleanup uses persisted locations rather than only current global setting");
  EncodeCache.ClearForJob(data,persisted);Check(true,"completed cache cleanup is idempotent");
 }
}
