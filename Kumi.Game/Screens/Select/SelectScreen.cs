using System.Diagnostics;
using Kumi.Game.Charts;
using Kumi.Game.Database;
using Kumi.Game.Input;
using Kumi.Game.Overlays;
using Kumi.Game.Screens.Play;
using Kumi.Game.Screens.Select.List;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;

namespace Kumi.Game.Screens.Select;

public partial class SelectScreen : ScreenWithChartBackground, IKeyBindingHandler<GlobalAction>
{
    public override float BlurAmount => 10f;
    public override float DimAmount => 0.5f;

    [Resolved]
    private MusicController musicController { get; set; } = null!;

    [Resolved]
    private IBindable<WorkingChart> chart { get; set; } = null!;
    
    private HalfOffsetContainer? bottomOffsetContainer;
    private ListSelect listSelect = null!;
    
    private IDisposable? realmSubscription;

    [BackgroundDependencyLoader]
    private void load(Bindable<WorkingChart> workingChart, ChartManager manager, RealmAccess realm)
    {
        InternalChildren = new Drawable[]
        {
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new []
                {
                    new Dimension(maxSize: 840),
                    new Dimension(GridSizeMode.Absolute, 32),
                    new Dimension()
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new BasicScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarVisible = true,
                            Masking = false,
                            Children = new Drawable[]
                            {
                                listSelect = new ListSelect
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Padding = new MarginPadding { Left = 32 },
                                    Children = new Drawable[]
                                    {
                                        new HalfOffsetContainer(this) { RelativeSizeAxes = Axes.X },
                                    }
                                },
                            }
                        },
                        Empty(), // padding
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Margin = new MarginPadding
                            {
                                Top = 32
                            },
                            RowDimensions = new[]
                            {
                                new Dimension(GridSizeMode.Absolute, 180),
                                new Dimension(GridSizeMode.AutoSize),
                                new Dimension(GridSizeMode.AutoSize)
                            },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new ChartSetInfoWedge
                                    {
                                        RelativeSizeAxes = Axes.Both
                                    }
                                }
                            }
                        }
                    },
                }
            }
        };
        
        listSelect.SelectedChart.BindValueChanged(c =>
        {
            workingChart.Value = manager.GetWorkingChart(c.NewValue);
        }, true);

        var charts = manager.GetAllUsableCharts();
        charts = charts.Where(c => c.Charts.Any() && !c.DeletePending).ToList();

        Schedule(() =>
        {
            realmSubscription = realm.Subscribe<ChartSetInfo>(r => r.All<ChartSetInfo>().Where(c => !c.DeletePending), (sender, changes) =>
            {
                if (changes == null)
                    return;
            
                foreach (var i in changes.DeletedIndices)
                    listSelect.RemoveChartSetAt(sender[i].ID);
            
                if (changes.InsertedIndices.Any())
                    bottomOffsetContainer?.Expire();
            
                foreach (var i in changes.InsertedIndices)
                    listSelect.AddChartSet(sender[i].Detach());
            
                if (changes.InsertedIndices.Any())
                {
                    bottomOffsetContainer = new HalfOffsetContainer(this) { RelativeSizeAxes = Axes.X };
                    listSelect.Add(bottomOffsetContainer);
                }
            });
            
            foreach (var set in charts)
                listSelect.AddChartSet(set);
            
            // TODO: Ideally, this should always be the last child no matter what.
            listSelect.Add(bottomOffsetContainer = new HalfOffsetContainer(this) { RelativeSizeAxes = Axes.X });
        });
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        this.FadeInFromZero(250);
        beginLooping();
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);
        
        beginLooping();
    }

    public override void OnSuspending(ScreenTransitionEvent e)
    {
        base.OnSuspending(e);
        
        endLooping();
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        endLooping();
        
        return base.OnExiting(e);
    }

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (e.Repeat || !this.IsCurrentScreen())
            return false;
        
        switch (e.Action)
        {
            case GlobalAction.Select:
                this.Push(new PlayerLoader());
                return true;
        }
        
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    private bool isHandlingLooping;

    private void beginLooping()
    {
        Debug.Assert(!isHandlingLooping);
        isHandlingLooping = true;
        
        ensureTrackLooping(chart.Value);

        musicController.TrackChanged += ensureTrackLooping;
    }

    private void endLooping()
    {
        if (!isHandlingLooping)
            return;

        musicController.CurrentTrack.Looping = isHandlingLooping = false;
        musicController.TrackChanged -= ensureTrackLooping;
    }

    private void ensureTrackLooping(IWorkingChart workingChart)
    {
        workingChart.PrepareTrackForPreview(true);
        musicController.Play(true);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        
        realmSubscription?.Dispose();
    }

    private partial class HalfOffsetContainer : CompositeDrawable
    {
        private readonly Drawable source;

        public HalfOffsetContainer(Drawable source)
        {
            this.source = source;
        }

        protected override void Update()
        {
            base.Update();
            
            Height = source.DrawHeight / 2;
        }
    }
}
