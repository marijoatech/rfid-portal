using System;
using System.IO;
using System.Xml.Serialization;

namespace PortalCalibration
{
    [Serializable]
    public sealed class PortalProfile
    {
        public string Ip;
        public int Channels;
        public int[] Powers;
        public bool[] Enabled;
        public int Q;
        public int Session;
        public int ScanTimeMs;
        public int RssiMin;
        public bool IgnoreBackground;
        public string[] Background;
    }

    public static class ProfileStore
    {
        public static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perfil_porton.xml"); }
        }

        public static void Save(PortalProfile profile)
        {
            if (profile == null) { throw new ArgumentNullException("profile"); }
            using (FileStream stream = File.Create(FilePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(PortalProfile));
                serializer.Serialize(stream, profile);
            }
        }

        public static PortalProfile Load()
        {
            if (!File.Exists(FilePath)) { return null; }
            using (FileStream stream = File.OpenRead(FilePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(PortalProfile));
                return (PortalProfile)serializer.Deserialize(stream);
            }
        }
    }
}
