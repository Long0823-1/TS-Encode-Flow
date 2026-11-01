using System;
using System.IO;
using System.Linq;
using System.Text;
using TsEncodeFlow;
static partial class Tests {
 static void LogoLibraryTests(string root) {
  string data=Path.Combine(root,"library"),original=Path.Combine(root,"manual-logo.lgd"),v2=Path.Combine(root,"manual-logo.lgd2");
  void Fixture(string file,bool version2){using var w=new BinaryWriter(File.Create(file));byte[] signature=new byte[28];Encoding.ASCII.GetBytes(version2?"<logo data file ver0.2>":"<logo data file ver0.1>").CopyTo(signature,0);w.Write(signature);w.Write(new byte[]{0,0,0,1});w.Write(new byte[version2?256:32]);foreach(short value in new short[]{4,4,16,16,0,0,0,0})w.Write(value);if(version2)w.Write(new byte[240]);for(int i=0;i<256;i++){w.Write((short)250);w.Write((short)4096);w.Write(new byte[8]);}}
  void RejectFile(Action action,string name){bool rejected=false;try{action();}catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException){rejected=true;}Check(rejected,name);}
  Fixture(original,false);Fixture(v2,true);var station=new StationLogo {Key="1-2-3-1440x1080",Channel="手動追加テスト局",Width=1440,Height=1080};
  var bitmap=LogoCandidates.Read(v2,StationLogos.Geometry(station));Check(bitmap.Width==16&&bitmap.X==4&&bitmap.Alpha[0]==64,"LGD v0.2 position, luma and transparency preview supported");
  string sourceId=Store.Fingerprint(original),source2Id=Store.Fingerprint(v2);var imported1=LogoLibrary.Import(data,station,original,sourceId);
  Check(File.ReadAllBytes(imported1).SequenceEqual(File.ReadAllBytes(original))&&Store.Fingerprint(original)==sourceId,"manual import copies LGD and preserves original source");
  Check(StationLogos.List(data).Single().Channel==station.Channel&&StationLogos.List(data).Single().Area.X==4,"manual station and source geometry persist without queue jobs");
  string imported2=LogoLibrary.Import(data,station,v2,source2Id);Check(StationLogos.Files(data,station.Key).Count==2&&StationLogos.Find(data,station.Key)==imported2,"import adds a version and selects it without removing previous logo");
  RejectFile(()=>LogoLibrary.Delete(data,station.Key,Path.GetFileName(imported2),"stale"),"changed logo cannot be deleted using stale preview");
  RejectFile(()=>LogoLibrary.Delete(data,station.Key,"../manual-logo.lgd",sourceId),"delete cannot escape station folder");
  Reject(()=>StationLogos.Describe("../../outside"),"station keys reject path traversal");
  Reject(()=>LogoLibrary.Import(data,new StationLogo {Key=station.Key,Width=1920,Height=1080},original,sourceId),"manual station dimensions must match key");
  RejectFile(()=>LogoLibrary.Import(data,station,original,"old source"),"changed import file must be selected and previewed again");
  RejectFile(()=>LogoLibrary.Import(data,new StationLogo {Key="1-2-3-8x8",Width=8,Height=8},original,sourceId),"out of bounds logo cannot be attached to wrong source geometry");
  Check(StationLogos.Files(data,"1-2-3-8x8").Count==0,"invalid import leaves no registered logo");
  LogoLibrary.Delete(data,station.Key,Path.GetFileName(imported2),Store.Fingerprint(imported2));Check(!File.Exists(imported2)&&StationLogos.Find(data,station.Key)==imported1,"deleting active version selects remaining latest logo");
  LogoLibrary.Delete(data,station.Key,Path.GetFileName(imported1),Store.Fingerprint(imported1));Check(StationLogos.Find(data,station.Key)==null&&StationLogos.Files(data,station.Key).Count==0&&StationLogos.List(data).Single().Area!=null,"last logo deletion removes file and manifest but retains station region");
  imported1=LogoLibrary.Import(data,station,original,sourceId);imported2=LogoLibrary.Import(data,station,v2,source2Id);LogoLibrary.Delete(data,station.Key,Path.GetFileName(imported1),Store.Fingerprint(imported1));Check(StationLogos.Find(data,station.Key)==imported2,"deleting older version preserves active logo");
  string bad=Path.Combine(root,"broken.lgd");File.WriteAllText(bad,"not a logo");RejectFile(()=>LogoLibrary.Import(data,station,bad,Store.Fingerprint(bad)),"malformed manual file cannot be installed");Check(StationLogos.Find(data,station.Key)==imported2,"failed import preserves previous active logo");
  using(var q=new QueueManager(Path.Combine(root,"library-queue"))) {
   string input=Path.Combine(root,"library-record.ts");File.WriteAllText(input,"fixture");var job=q.Enqueue(input);job.Metadata=new Metadata {NetworkId=1,TransportId=2,ServiceId=3,Width=1440,Height=1080};job.State=State.NeedsLogo;q.Paused=true;
   QueueManager.Request Request(string action)=>new(){Action=action,LogoOperationId=Guid.NewGuid().ToString("N"),LogoStation=station,LogoSource=original,LogoSourceFingerprint=sourceId};
   LogoOperationResult Result(QueueManager.Request req)=>Store.Load<LogoOperationResult>(Path.Combine(q.DataFolder,"LogoOperations",req.LogoOperationId+".json"),()=>throw new Exception("receipt missing"));
   var request=Request("import-logo");q.ManageLogo(request);Check(Result(request).Success&&job.State==State.Waiting&&!q.Paused,"worker acknowledges import and resumes matching logo wait");
   string installed=StationLogos.Find(q.DataFolder,station.Key);var deletion=Request("delete-logo");deletion.LogoFileName=Path.GetFileName(installed);deletion.Fingerprint=Store.Fingerprint(installed);job.State=State.Running;q.ManageLogo(deletion);Check(!Result(deletion).Success&&File.Exists(installed),"worker rejects deleting logo while its station is running");
   var blockedImport=Request("import-logo");q.ManageLogo(blockedImport);Check(!Result(blockedImport).Success&&StationLogos.Files(q.DataFolder,station.Key).Count==1,"worker rejects import while its station is running");
   job.Metadata=null;blockedImport=Request("import-logo");q.ManageLogo(blockedImport);Check(!Result(blockedImport).Success,"worker protects logo library while running job station is unknown");
   job.State=State.Cancelled;q.Paused=true;deletion.LogoOperationId=Guid.NewGuid().ToString("N");string inbox=Path.Combine(q.DataFolder,"Inbox",deletion.LogoOperationId+".json");Store.Save(inbox,deletion);q.Tick();Check(Result(deletion).Success&&!File.Exists(installed)&&!File.Exists(inbox),"worker inbox performs scoped delete and writes acknowledgement");
   using(var held=File.Open(Path.Combine(q.DataFolder,"Logos",station.Key+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){var busy=Request("import-logo");q.ManageLogo(busy);Check(!Result(busy).Success&&StationLogos.Find(q.DataFolder,station.Key)==null,"generation lock prevents conflicting library mutation");}
   job.Metadata=new Metadata {NetworkId=1,TransportId=2,ServiceId=3,Width=1440,Height=1080};job.State=State.NeedsLogoReview;job.LogoCandidate=original;
   var discard=Request("delete-logo-candidate");discard.JobId=job.Id;discard.Fingerprint=job.Fingerprint;discard.LogoSourceFingerprint=sourceId;q.ManageLogo(discard);Check(!Result(discard).Success&&File.Exists(original),"candidate deletion cannot touch files outside its own job folder");
   string candidate=Path.Combine(Commercials.Folder(job,q.DataFolder),"candidate.lgd");Directory.CreateDirectory(Path.GetDirectoryName(candidate));File.Copy(original,candidate);job.LogoCandidate=candidate;
   discard.LogoOperationId=Guid.NewGuid().ToString("N");discard.LogoSourceFingerprint=Store.Fingerprint(candidate);q.ManageLogo(discard);Check(Result(discard).Success&&!File.Exists(candidate)&&job.State==State.NeedsLogo&&job.LogoCandidate=="","unapproved candidate can be deleted and returns to region selection");
  }
 }
}
