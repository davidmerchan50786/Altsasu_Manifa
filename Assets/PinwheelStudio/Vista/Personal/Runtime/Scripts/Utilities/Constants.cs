#if VISTA
using System;

namespace Pinwheel.Vista
{
    public static class Constants
    {
        public static int K1K24
        {
            get
            {
                return 0b0100_0000_0000;
            }
        }

        public static int K1K25
        {
            get
            {
                return 0b0100_0000_0001;
            }
        }

        public static int RES_MIN
        {
            get
            {
                return 0b0000_0010_0000;
            }
        }

        internal static event Func<int> getResMaxCallback;
        public static int RES_MAX
        {
            get
            {
                if (getResMaxCallback != null)
                {
                    return getResMaxCallback.Invoke();
                }
                else
                {
                    return 0b0010_0000_0000;
                }
            }
        }

        public static int HM_RES_MIN
        {
            get
            {
                return 0b0000_0010_0001;
            }
        }

        internal static event Func<int> getHmResMaxCallback;
        public static int HM_RES_MAX
        {
            get
            {
                if (getHmResMaxCallback != null)
                {
                    return getHmResMaxCallback.Invoke();
                }
                else
                {
                    return 0b0010_0000_0001;
                }
            }
        }
    }
}
#endif


