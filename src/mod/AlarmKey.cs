namespace SolarExpanseLaunchWindows
{
    internal struct AlarmKey : System.IEquatable<AlarmKey>
    {
        public string OriginId;
        public string DestId;
        public int    Year;
        public int    Month;
        public bool   IsFastest;

        public bool Equals(AlarmKey o) =>
            OriginId == o.OriginId && DestId == o.DestId && Year == o.Year && Month == o.Month && IsFastest == o.IsFastest;
        public override bool Equals(object obj) => obj is AlarmKey k && Equals(k);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = OriginId?.GetHashCode() ?? 0;
                h = h * 31 + (DestId?.GetHashCode() ?? 0);
                h = h * 31 + Year;
                h = h * 31 + Month;
                h = h * 31 + (IsFastest ? 1 : 0);
                return h;
            }
        }
    }
}
