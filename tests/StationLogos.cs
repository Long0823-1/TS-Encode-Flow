using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task StationLogoTests(string root) {
  string data=Path.Combine(root,"station-logos"),input=Path.Combine(root,"station-source.ts");File.WriteAllText(input,"fixture");
  var m=new Metadata {NetworkId=1,TransportId=2,ServiceId=3,Channel="テスト局",Width=1440,Height=1080};var j=new Job {Input=input,Metadata=m,Preset=new Preset()};
  Check(StationLogos.Key(j)=="1-2-3-1440x1080","station key preserves existing station/resolution logo cache");
  bool needs=false;try{StationLogos.Require(data,j,new Settings());}catch(LogoAreaRequiredException){needs=true;}Check(needs,"new station requires preview selection before expensive indexing");
  j.Preset.CmFallbackWithoutLogo=true;needs=false;try{StationLogos.Require(data,j,new Settings());}catch(LogoAreaRequiredException){needs=true;}Check(needs,"fallback does not skip initial station region setup");
  j.Preset.CmNoLogo=true;StationLogos.Require(data,j,new Settings());j.Preset.CmNoLogo=false;StationLogos.Require(data,j,new Settings {CmLogo="manual.lgd"});
  var area=LogoArea.FromSelection(.9,.2,.8,.1,1440,1080);Check(area.X==1152&&area.Y==108&&area.Width==144&&area.Height==108,"reverse preview selection maps proportionally to original anamorphic pixels");
  Reject(()=>new LogoArea {X=1438,Y=0,Width=8,Height=8}.Validate(m),"out of frame logo selection rejected");Reject(()=>new LogoArea {X=1,Y=1,Width=7,Height=8}.Validate(m),"tiny logo selection rejected");
  StationLogos.SaveArea(data,j,area);StationLogos.Require(data,j,new Settings());Check(StationLogos.Read(data,j).Area.ToString()==area.ToString(),"station region persists and is reusable without another prompt");
  Check(StationLogos.List(data).Single().Channel==m.Channel,"station management survives clearing queue history");
  string key=StationLogos.Key(j),folder=Path.Combine(data,"Logos"),logo=Path.Combine(folder,key+"-v0001.lgd");File.WriteAllText(logo,"legacy logo");File.WriteAllText(Path.Combine(folder,key+".latest"),"1\n"+Path.GetFileName(logo)+"\n");
  Check(StationLogos.Find(data,j)==logo,"existing native latest manifest reused");
  File.WriteAllText(Path.Combine(folder,key+".latest"),"1\n../../outside.lgd\n");Check(StationLogos.Find(data,j)==logo,"manifest traversal ignored and station versions used");
  File.WriteAllText(Path.Combine(folder,key+"-v0002.lgd"),"");Check(StationLogos.Find(data,j)==logo,"empty generated logo never reused");
  var other=Store.Clone(j);other.Metadata.ServiceId++;Check(StationLogos.Find(data,other)==null&&StationLogos.Read(data,other).Area==null,"different stations cannot reuse each other's logo or region");
  other=Store.Clone(j);other.Metadata.Width=1920;Check(StationLogos.Find(data,other)==null&&StationLogos.Read(data,other).Area==null,"different source resolutions require separate logo setup");
  other=Store.Clone(j);other.Metadata.NetworkId=0;string unknown=StationLogos.Key(other);other.Metadata.Channel="別の局";Check(unknown!=StationLogos.Key(other),"incomplete broadcast IDs isolate station names");
  other.Metadata.Channel="";unknown=StationLogos.Key(other);other.Input=input+"other.ts";Check(unknown!=StationLogos.Key(other),"unknown station without name is isolated by source path");
  using(var lease=await StationLogos.Lock(data,j,CancellationToken.None))using(var cts=new CancellationTokenSource(100)) {bool cancelled=false;try{using var second=await StationLogos.Lock(data,j,cts.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"concurrent station logo generation lock is cancellable");}
  using(var q=new QueueManager(Path.Combine(root,"logo-queue"))) {
   q.Paused=true;var first=q.Enqueue(input);first.Preset.CmMode=CmMode.Chapters;first.Preset.CmSource=CmSource.Amatsukaze;first.Metadata=Store.Clone(m);first.State=State.NeedsLogo;
   Check(q.Enqueue(input)==null,"waiting for logo does not allow duplicate same recording");
   var second=new Job {Input=input,Metadata=Store.Clone(m),Preset=new Preset(),State=State.NeedsLogo};q.Jobs.Add(second);
   Reject(()=>q.SetLogoArea(new QueueManager.Request {JobId=first.Id,Fingerprint="stale",LogoArea=area}),"changed input cannot accept stale logo selection");
   string originalFingerprint=first.Fingerprint;first.Fingerprint="old metadata";Reject(()=>q.SetLogoArea(new QueueManager.Request {JobId=first.Id,Fingerprint=Store.Fingerprint(input),LogoArea=area}),"current preview cannot apply an area using stale recording metadata");first.Fingerprint=originalFingerprint;
   q.SetLogoArea(new QueueManager.Request {JobId=first.Id,Fingerprint=Store.Fingerprint(input),LogoArea=area});
   Check(first.State==State.Waiting&&second.State==State.Waiting&&!q.Paused,"station region confirmation resumes all pending jobs for that station");
   Check(StationLogos.Read(q.DataFolder,first).Area.ToString()==area.ToString(),"worker persists station selection before resuming jobs");
   first.State=State.NeedsLogo;q.Cancel(first.Id);Check(first.State==State.Cancelled,"logo setup wait can be cancelled");
   Check(!QueueManager.IsHistory(State.NeedsLogo),"history clearing preserves station setup wait");
   string candidate=Path.Combine(Commercials.Folder(first,q.DataFolder),"candidate.lgd");Directory.CreateDirectory(Path.GetDirectoryName(candidate));
   using(var writer=new BinaryWriter(File.Create(candidate))){var signature=new byte[28];System.Text.Encoding.ASCII.GetBytes("<logo data file ver0.1>").CopyTo(signature,0);writer.Write(signature);writer.Write(new byte[]{0,0,0,1});writer.Write(new byte[32]);foreach(short v in new short[]{4,4,16,16,0,0,0,0})writer.Write(v);for(int i=0;i<256;i++){writer.Write((short)250);writer.Write((short)4096);writer.Write(new byte[8]);}}
   var bitmap=LogoCandidates.Read(candidate,m);Check(bitmap.Width==16&&bitmap.Alpha[0]==64&&bitmap.Luma[0]==235,"generated LGD pixels and opacity can be previewed");
   first.State=State.NeedsLogoReview;first.LogoCandidate=candidate;q.Paused=true;
   Check(!QueueManager.IsHistory(first.State)&&q.Enqueue(input)==null,"unapproved logo candidate stays pending and prevents duplicate input");
   Reject(()=>q.ReviewLogo(new QueueManager.Request {JobId=first.Id,Fingerprint="stale"},true),"stale candidate confirmation is rejected");
   q.ReviewLogo(new QueueManager.Request {JobId=first.Id,Fingerprint=first.Fingerprint},false);Check(first.State==State.NeedsLogo&&StationLogos.Find(q.DataFolder,first)==null,"rejecting contaminated candidate does not register station logo");
   first.State=State.NeedsLogoReview;first.LogoCandidate=candidate;second.State=State.NeedsLogoReview;q.ReviewLogo(new QueueManager.Request {JobId=first.Id,Fingerprint=first.Fingerprint},true);
   Check(first.State==State.Waiting&&!q.Paused&&File.ReadAllBytes(StationLogos.Find(q.DataFolder,first)).SequenceEqual(File.ReadAllBytes(candidate)),"approved candidate is saved before automatic resume");
   Check(second.State==State.Waiting,"one approval resumes other pending jobs from the same station");
   first.LogoCandidate=Path.Combine(root,"outside.lgd");Reject(()=>LogoCandidates.Promote(q.DataFolder,first),"candidate outside its job folder cannot be promoted");
   bool malformed=false;File.WriteAllText(candidate,"broken");try{LogoCandidates.Read(candidate,m);}catch(InvalidDataException){malformed=true;}Check(malformed,"malformed LGD cannot be previewed or approved");
  }
 }
}
