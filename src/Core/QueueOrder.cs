using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public static class QueueOrder {
  public static bool CanCancel(State state)=>state==State.Running||state==State.Waiting||state==State.NeedsInput||state==State.NeedsCmReview||state==State.NeedsLogo||state==State.NeedsLogoReview;
  public static int Rank(State state)=>state==State.Running?0:state==State.Waiting?1:QueueManager.IsHistory(state)?3:2;
  // The scheduler consumes the persisted list in insertion order. Do not sort
  // waiting rows by a display-only timestamp or rebuild existing row objects.
  public static List<Job> Display(IEnumerable<Job> jobs)=>jobs.Select((job,index)=>new{job,index}).OrderBy(x=>Rank(x.job.State)).ThenBy(x=>Rank(x.job.State)==3?-x.job.Created.Ticks:x.index).ThenBy(x=>x.index).Select(x=>x.job).ToList();
 }
}
