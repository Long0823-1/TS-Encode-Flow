using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task BatchProfiles(string root) {
  string data=Path.Combine(root,"BatchProfiles"),input=Path.Combine(root,"BAT & ! 日本語.ts");File.WriteAllText(input,"fixture");
  var settings=new Settings {OutputFolder=Path.Combine(root,"BatOutput"),FileName="{Title}"};
  var p=new Preset {Name="BAT専用",Engine=Engine.VCEEncC,Codec=Codec.HEVC,Container=Container.mkv,Quality=29,Width=1280,Height=720,RepairTimeline=true,RemoveLogo=true,SubtitleMode=SubtitleMode.AribRaw,CmMode=CmMode.Chapters,CmSource=CmSource.Amatsukaze,CmFallbackWithoutLogo=true};
  string bat=Path.Combine(root,"録画後.bat");Integration.SaveBat(bat,Path.Combine(root,"worker.exe"),data,settings,p,false);
  var profile=Store.Load<BatchProfile>(bat+".profile.json",()=>null);string expected=Store.Json.Serialize(p);p.Quality=10;
  Check(Store.Json.Serialize(profile.Preset)==expected,"BAT freezes every preset option independently of later GUI edits");
  using(var queue=new QueueManager(data)) {
   queue.Settings=settings;queue.Paused=true;var job=queue.Enqueue(input,true);job.Message="watch";
   var applied=queue.Enqueue(input,false,profile.Preset,profile.OutputFolder,profile.FileName);
   Check(job==applied&&queue.Jobs.Count==1&&job.PresetSource=="bat"&&Store.Json.Serialize(job.Preset)==expected&&job.OutputFolder==profile.OutputFolder,"BAT replaces a not-started watcher job with its complete frozen profile");
   job.State=State.Running;
   Check(queue.Enqueue(input,false,new Preset(),settings.OutputFolder,"{Title}")==null&&Store.Json.Serialize(job.Preset)==expected,"BAT never modifies an active encoding job");
  }
  bool rejected=false;try{QueueManager.Submit(data,new[]{input},new BatchProfile());}catch(InvalidDataException){rejected=true;}Check(rejected,"malformed BAT profile cannot silently fall back to GUI preset");
  string zip=Path.Combine(root,"fake-update.zip");File.WriteAllText(zip,"fixture");rejected=false;try{await SelfUpdate.Verify(zip,"",default);}catch(InvalidDataException){rejected=true;}Check(rejected,"self update requires a published SHA-256");
  rejected=false;try{await SelfUpdate.Verify(zip,"sha256:"+new string('0',64),default);}catch(InvalidDataException){rejected=true;}Check(rejected,"self update rejects a bad SHA-256");
  var m=new Metadata {Duration=100,VideoStart=2,AudioStart=0};Check(CmAudio.Filter(m).Contains("atrim=start=2")&&CmAudio.Filter(m).EndsWith("atrim=duration=100"),"CM PCM audio is aligned and bounded to verified video");
  settings.Presets.Add(p);p.FileName="{TitleWithoutEpisode}_{Episode}";
  Integration.SaveBat(bat,Path.Combine(root,"worker.exe"),data,settings,p);
  var linked=Store.Load<BatchProfile>(bat+".profile.json",()=>null);
  var first=linked.Resolve(settings);p.Quality=33;p.FileName="{Channel}/{Title}";
  var latest=linked.Resolve(settings);
  Store.Save(Path.Combine(data,"settings.json"),settings);
  Check(linked.Resolve(Integration.LoadSettings(data)).Preset.Quality==33,"BAT identity and latest preset survive settings reload across worker processes");
  Check(latest.Preset.Quality==33&&latest.FileName==p.FileName&&first.Preset.Quality!=33,"linked BAT resolves latest settings but previously submitted snapshot stays unchanged");
  PresetCatalog.SaveCustom(settings,Store.Clone(p));
  Check(linked.Resolve(settings).Preset.Quality==33,"same-name preset overwrite preserves BAT identity");
  var chosen=settings.Presets.Single(x=>BatchProfile.Key(x)==linked.LivePresetKey);settings.Presets.Remove(chosen);
  rejected=false;try{linked.Resolve(settings);}catch(InvalidDataException){rejected=true;}Check(rejected,"linked BAT fails explicitly after target preset deletion");
  using(var queue=new QueueManager(Path.Combine(root,"PresetMacros"))) {
   queue.Settings=settings;queue.Paused=true;settings.SelectedPreset=0;settings.Presets[0].FileName="{Channel}/{Title}";
   var added=queue.Enqueue(input);Check(added.FileName=="{Channel}/{Title}","manual queue uses preset filename macro");
   queue.Cancel(added.Id);settings.Presets[0].FileName="";Check(queue.Enqueue(input).FileName==settings.FileName,"empty preset macro falls back to common setting");
  }
 }
}
