using GStudio.Common.Configuration;

namespace GStudio.Cinematic.Engine;

public static class SpringPresetMap
{
    public static SpringSettings Get(MotionPreset preset)
    {
        return preset switch
        {
            MotionPreset.Slow => new SpringSettings(Tension: 85.0d, Friction: 28.0d, Mass: 1.0d),
            MotionPreset.Mellow => new SpringSettings(Tension: 120.0d, Friction: 30.0d, Mass: 1.0d),
            MotionPreset.Quick => new SpringSettings(Tension: 175.0d, Friction: 34.0d, Mass: 1.0d),
            MotionPreset.Rapid => new SpringSettings(Tension: 230.0d, Friction: 38.0d, Mass: 1.0d),
            _ => new SpringSettings(Tension: 175.0d, Friction: 34.0d, Mass: 1.0d)
        };
    }
}
