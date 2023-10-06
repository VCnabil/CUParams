using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using canlibCLSNET;

namespace CANUtils
{
    class CANHandler
    {
        int handle = -1, channel;
        Thread? runthread = null;
        CancellationTokenSource ct = new CancellationTokenSource();

        public CANHandler(string devdescr, int channo, Action<uint, byte[]> callback, int bitrate = Canlib.canBITRATE_250K)
        {
            Init(GetCANChannels(devdescr)[channo], callback, bitrate);
        }

        public CANHandler(string channame, Action<uint, byte[]> callback, int bitrate = Canlib.canBITRATE_250K)
        {
            Init(channame, callback, bitrate);
        }

        void Init(string channame, Action<uint, byte[]> callback, int bitrate = Canlib.canBITRATE_250K)
        {
            var chans = GetCANChannels();

            for (channel = 0; channel < chans.Length; channel++)
            {
                if (chans[channel] == channame)
                    break;
            }

            handle = Canlib.canOpenChannel(channel, Canlib.canOPEN_ACCEPT_VIRTUAL);
            if (handle < 0)
                throw new Exception("Unable to open channel");

            Canlib.canSetBusParams(handle, bitrate, 0, 0, 0, 0, 0);

            if (Canlib.canBusOn(handle) != Canlib.canStatus.canOK)
                throw new Exception("Unable to go on the bus");

            runthread = new Thread(() => Run(callback));

            runthread.Start();
        }

        public class CANChannelInfo
        {
            public ulong UPC { get; }
            public long SerialNo { get; }
            public string DevDescr { get; }
            public string ChanName { get; }
            public uint ChanNo { get; }

            public int GlobChanNo { get; }

            public CANChannelInfo(int channel)
            {
                GlobChanNo = channel;

                UPC = (ulong)GetItem(Canlib.canCHANNELDATA_CARD_UPC_NO);
                SerialNo = (long)GetItem(Canlib.canCHANNELDATA_CARD_SERIAL_NO);
                DevDescr = (string)GetItem(Canlib.canCHANNELDATA_DEVDESCR_ASCII);
                ChanName = (string)GetItem(Canlib.canCHANNELDATA_CHANNEL_NAME);
                ChanNo = (uint)GetItem(Canlib.canCHANNELDATA_CHAN_NO_ON_CARD);
            }

            object GetItem(int item)
            {
                object o;

                Canlib.canGetChannelData(GlobChanNo, item, out o);

                return o;
            }
        }

        public static List<CANChannelInfo> GetCANChannelInfo()
        {
            int channels, i;
            List<CANChannelInfo> chaninfo = new List<CANChannelInfo>();

            Canlib.canInitializeLibrary();

            if (Canlib.canGetNumberOfChannels(out channels) != Canlib.canStatus.canOK)
                return chaninfo;

            for (i = 0; i < channels; i++)
                chaninfo.Add(new CANChannelInfo(i));

            return chaninfo;
        }

        public static string[] GetCANChannels(string? dev = null)
        {
            var ci = GetCANChannelInfo();

            if (dev == null)
                return ci.Select(a => a.ChanName).ToArray();
            else
                return ci.Where(a => a.DevDescr == dev).OrderBy(a => a.ChanNo).Select(a => a.ChanName).ToArray();
        }

        public static string[] Devices => GetCANChannelInfo().Select(a => a.DevDescr).Distinct().ToArray();

        public string GetChannel()
        {
            object o;

            Canlib.canGetChannelData(channel, Canlib.canCHANNELDATA_CHANNEL_NAME, out o);

            return (string)o;
        }

        public void Close()
        {
            if (handle == -1)
                return;

            ct.Cancel();

            runthread?.Join();

            Canlib.canClose(handle);

            handle = -1;
        }

        public void SendCAN(uint id, byte[] data)
        {
            Canlib.canWrite(handle, (int)id, data, 8, Canlib.canMSG_EXT);
        }

        void Run(Action<uint, byte[]> callback)
        {
            Canlib.canStatus status;
            int id, dlc, flag;
            long time;
            byte[] data = new byte[8];

            while (!ct.IsCancellationRequested)
            {
                status = Canlib.canReadWait(handle, out id, data, out dlc, out flag, out time, 100);
                if (status == Canlib.canStatus.canERR_NOMSG)
                    continue;
                else if (status != Canlib.canStatus.canOK)
                    break;

                callback((uint)id, data);
            }
        }
    }
}
