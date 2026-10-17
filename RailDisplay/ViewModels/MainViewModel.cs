using System;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Core.Controllers.Audio;

namespace RailDisplay.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public CtcsViewModel Ctcs { get; }
    public IDeviceAudioController CtcsAudio { get; }

    public MainViewModel(CtcsViewModel ctcs, IDeviceAudioController ctcsAudio)
    {
        ArgumentNullException.ThrowIfNull(ctcs);
        ArgumentNullException.ThrowIfNull(ctcsAudio);
        Ctcs = ctcs;
        CtcsAudio = ctcsAudio;
    }
}
