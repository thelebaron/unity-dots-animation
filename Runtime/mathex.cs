using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace AnimationSystem
{
    public static class mathex
    {
        
        /// <summary>Returns b if c is true, a otherwise.</summary>
        /// <param name="a">Value to use if c is false.</param>
        /// <param name="b">Value to use if c is true.</param>
        /// <param name="c">Bool value to choose between a and b.</param>
        /// <returns>The selection between a and b according to bool c.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion select(quaternion a, quaternion b, bool c) { return c ? b : a; }
    }
}