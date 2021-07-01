// for batching
namespace DOTSNET
{
    public class Batch
    {
        // each batch needs a writer for batching
        public BitWriter writer;

        // each channel's batch has its own lastSendTime.
        // (use NetworkTime for maximum precision over days)
        //
        // channel batches are full and flushed at different times. using
        // one global time wouldn't make sense.
        // -> we want to be able to reset a channels send time after Send()
        //    flushed it because full. global time wouldn't allow that, so
        //    we would often flush in Send() and then flush again in Update
        //    even though we just flushed in Send().
        // -> initialize with current time so first update doesn't calculate
        //    elapsed via 'now - 0'
        public double lastSendTime;

        public Batch(int MaxMessageSize, double currentTime)
        {
            writer = new BitWriter(new byte[MaxMessageSize]);
            lastSendTime = currentTime;
        }
    }
}