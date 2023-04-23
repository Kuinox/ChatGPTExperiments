using Discord.Interactions;

namespace KuinoxSemiAGI;

public partial class ImageService
{
    public enum RotateOption
    {
        [ChoiceDisplay( "Rotate 90 degrees clockwise." )]
        Rotate90 = 90,

        [ChoiceDisplay( "Rotate 180 degrees clockwise." )]
        Rotate180 = 180,

        [ChoiceDisplay( "Rotate 270 degrees clockwise." )]
        Rotate270 = 270
    }
}
