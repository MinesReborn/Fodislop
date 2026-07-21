using System.Security.Cryptography;
using System.Text;

namespace Fodinae.Scripts
{
    public static class ETagCalculator
    {
        public static string Calculate(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(data);
                var sb = new StringBuilder();
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
