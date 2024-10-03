namespace Sapling.Engine
{
    public static class MathHelpers
    {
        public static float[] LogLookup = new float[230];
        static MathHelpers()
        {
            for (var i = 0; i < LogLookup.Length; i++)
            {
                LogLookup[i] = (float)Math.Log(i);
            }
        }
    }
}
