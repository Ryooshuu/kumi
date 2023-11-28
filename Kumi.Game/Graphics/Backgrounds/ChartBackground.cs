﻿using Kumi.Game.Charts;
using Kumi.Game.Screens.Backgrounds;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;

namespace Kumi.Game.Graphics.Backgrounds;

public partial class ChartBackground : BackgroundScreen
{
    [Resolved]
    private LargeTextureStore textures { get; set; } = null!;
    
    [BackgroundDependencyLoader]
    private void load(IBindable<WorkingChart> workingChart)
    {
        workingChart.BindValueChanged(onNewChart, true);
    }

    private void onNewChart(ValueChangedEvent<WorkingChart> chart)
    {
        var background = new Background();
        background.SetBackground(chart.NewValue.GetBackground() ?? textures.Get("Backgrounds/default"));
        
        BackgroundStack.Push(background, easing: Easing.OutQuint);
    }
}
