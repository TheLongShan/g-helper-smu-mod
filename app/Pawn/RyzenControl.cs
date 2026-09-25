using System;
using GHelper.Mode;

namespace PawnIO
{
    public static class RyzenControl
    {
        public static SmuStatus SetFMax(uint mhz)
        {
            var smu = ModeControl.GetSmu();
            if (smu == null)
            {
                Logger.WriteLine("RyzenControl.SetFMax: SMU not available");
                return SmuStatus.Failed;
            }

            var status = smu.SetFMax(mhz);
            Logger.WriteLine($"RyzenControl.SetFMax: {mhz} MHz -> {status}");
            return status;
        }

        public static uint? GetFMax()
        {
            var smu = ModeControl.GetSmu();
            return smu?.GetFMax();
        }

        public static bool CanSetFMax()
        {
            var smu = ModeControl.GetSmu();
            return smu != null && smu.CanSetFMax();
        }
    }
}
