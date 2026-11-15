using System;
using System.IO;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void StoreSaveTests(string root) {
  var named=new Metadata {Title="探偵はもう、死んでいる。Season2　＃5",Episode="5"};
  Check(Names.Render("{if:Episode}{TitleWithoutEpisode}　第{Episode}話{else}{Title}{endif}",named,new Preset {Container=Container.mkv})=="探偵はもう、死んでいる。Season2　第5話.mkv","title macro removes matching episode while preserving Season2");
  named.Title="番組 第６話";Check(Names.Render("{TitleWithoutEpisode}",named,new Preset())=="番組 第６話.mp4","title macro preserves episode numbers that do not match metadata");
  var audioJob=new Job();var audioSettings=new Settings {EncodeCacheFolder=Path.Combine(root,"CustomCache")};
  string pcm=CmAudio.TemporaryPath(audioJob,audioSettings,root);
  Check(pcm.StartsWith(EncodeCache.Root(root,audioSettings.EncodeCacheFolder)+Path.DirectorySeparatorChar)&&audioJob.EncodeCacheFolders.Contains(audioSettings.EncodeCacheFolder),"CM PCM uses selected managed cache and registers for cleanup");
  Check(pcm!=CmAudio.TemporaryPath(audioJob,audioSettings,root),"parallel CM attempts have unique PCM files");
  string folder=Path.Combine(root,"AtomicSave");Directory.CreateDirectory(folder);
  foreach(bool backup in new[]{false,true}) {
   string file=Path.Combine(folder,backup?"backup.json":"queue.json");Store.Save(file,new[]{1});Store.Save(file,new[]{2});
   var held=new FileStream(backup?file+".bak":file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
   var release=Task.Run(async()=>{await Task.Delay(180);held.Dispose();});
   Store.Save(file,new[]{3});release.GetAwaiter().GetResult();
   Check(Store.Load(file,()=>Array.Empty<int>())[0]==3&&Store.Load(file+".bak",()=>Array.Empty<int>())[0]==2,"atomic save retries transient "+(backup?"backup":"destination")+" delete lock");
  }
  string locked=Path.Combine(folder,"locked.json");Store.Save(locked,new[]{7});
  using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
   bool failed=false;try{Store.Save(locked,new[]{9});}catch(IOException e){failed=e.Message.Contains(locked);}
   Check(failed&&Store.Load(locked,()=>Array.Empty<int>())[0]==7,"persistent save lock reports path and preserves prior data");
  }
  Check(Directory.GetFiles(folder,"*.tmp").Length==0,"failed and retried saves clean their own temporary files");
 }
}
