using System;
using System.IO;
using System.Linq;
using TsEncodeFlow;
static partial class Tests {
 static void StorageChecks(string root) {
  string data=Path.Combine(root,"ReadonlyCache"),id=Guid.NewGuid().ToString("N"),cache=Path.Combine(EncodeCache.Root(data),id,"Work","runtime");Directory.CreateDirectory(cache);string dll=Path.Combine(cache,"runtime.dll");File.WriteAllText(dll,"fixture");File.SetAttributes(dll,File.GetAttributes(dll)|FileAttributes.ReadOnly);EncodeCache.Clear(data,id);Check(!Directory.Exists(Path.Combine(EncodeCache.Root(data),id)),"completed cache cleanup removes copied readonly DLLs and parent tree");
  string error="Invalid frame dimensions 0x0.\nAAC bitstream not in ADTS format\nNo space left on device";Check(StorageSpace.Failure(error,-28,Path.Combine(root,"out.mkv")).StartsWith("保存先の空き容量"),"disk-full failure takes precedence over earlier AAC/video warnings");
  string empty=Path.Combine(root,"empty.ts");File.WriteAllText(empty,"");Check(Converter.ClosedEmptyRecording(empty),"closed zero byte recording is identified");using(var writer=File.Open(empty,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))Check(!Converter.ClosedEmptyRecording(empty),"active zero byte recording is not declared failed while writer holds it");
  File.SetLastWriteTimeUtc(empty,DateTime.UtcNow.AddMinutes(-10));using var queue=new QueueManager(Path.Combine(root,"EmptyQueue"));queue.Enqueue(empty);queue.Tick();Check(queue.Jobs.Single().State==State.Failed&&queue.Jobs.Single().Message.Contains("0バイト"),"stable closed zero byte TS fails clearly instead of waiting forever");
  var decoded=Enumerable.Range(0,1000).Select(i=>i/30d).ToArray();var indexed=(double[])decoded.Clone();for(int i=10;i<900;i+=10)indexed[i]+=(1/30d);Check(CmIndex.Align(indexed,decoded,1/30d)==0,"repair mode accepts many isolated one-frame decoder differences with exact anchors on each side");
  bool stopped=false;try{CmIndex.Align(indexed,decoded);}catch(InvalidDataException){stopped=true;}Check(stopped,"normal mode remains strict for isolated frame differences");
  indexed[999]+=1/30d;stopped=false;try{CmIndex.Align(indexed,decoded,1/30d);}catch(InvalidDataException){stopped=true;}Check(stopped,"repair mode rejects a terminal difference without a closing exact anchor");indexed[999]=decoded[999];
  stopped=false;for(int i=100;i<103;i++)indexed[i]=decoded[i]+1/30d;try{CmIndex.Align(indexed,decoded,1/30d);}catch(InvalidDataException){stopped=true;}Check(stopped,"repair mode still rejects sustained decoder misalignment");
  string index=Path.Combine(root,"incomplete.lwi");File.WriteAllText(index,"<LSMASHWorksIndexVersion=0.0.2.0>\n<LibavReaderIndexFile=13>\nIndex=0\n");Check(!CmIndex.Complete(index),"truncated CM index is rejected before passing it to native readers");File.AppendAllText(index,"</LibavReaderIndex>\n\0binary trailer");Check(CmIndex.Complete(index),"complete CM index accepts its valid binary trailer after the closing marker");
 }
}
