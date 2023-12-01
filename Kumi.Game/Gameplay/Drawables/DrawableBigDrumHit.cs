﻿using System.Diagnostics;
using Kumi.Game.Charts.Objects;
using Kumi.Game.Charts.Objects.Windows;
using Kumi.Game.Gameplay.Drawables.Parts;
using Kumi.Game.Input;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;

namespace Kumi.Game.Gameplay.Drawables;

public partial class DrawableBigDrumHit : DrawableNote<DrumHit>, IKeyBindingHandler<GameplayAction>
{
    private readonly Drawable? drumHitPart;
    
    public DrawableBigDrumHit(DrumHit note)
        : base(note)
    {
        RelativeSizeAxes = Axes.Y;
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.Centre;
        
        AddInternal(drumHitPart = createDrawable(new BigDrumHitPart(note.Type)));
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        Width = DrawSize.Y;
    }

    private Drawable createDrawable(Drawable drawable)
        => drawable.With(d =>
        {
            d.Anchor = Anchor.Centre;
            d.Origin = Anchor.Centre;
            d.RelativeSizeAxes = Axes.Both;
        });

    protected override void UpdateHitStateTransforms(NoteState newState)
    {
        switch (newState)
        {
            case NoteState.Hit:
                drumHitPart.MoveToY(-100, 250, Easing.OutBack);
                drumHitPart.FadeOut(250, Easing.OutQuint);
                
                this.Delay(250).Expire();
                break;
            case NoteState.Miss:
                drumHitPart!.FadeColour(Color4Extensions.FromHex("0D0D0D").Opacity(0.5f), 100);
                drumHitPart.FadeOut(100);

                this.Delay(100).Expire();
                break;
        }
    }

    protected override void CheckForResult(bool userTriggered, double deltaTime)
    {
        Debug.Assert(Note.Windows != null);

        if (!userTriggered)
        {
            if (Time.Current > Note.StartTime - Note.Windows.WindowFor(NoteHitResult.Bad) && !Note.Windows.IsWithinWindow(deltaTime))
                ApplyResult(NoteHitResult.Miss);
            
            return;
        }

        var result = Note.Windows.ResultFor(deltaTime);
        if (result == null)
            return;
        
        ApplyResult(result.Value);
    }

    public bool OnPressed(KeyBindingPressEvent<GameplayAction> e)
    {
        if (Judged)
            return false;

        // TODO: Detect if the user is pressing both keys, and update the score accordingly.
        switch (Note.Type)
        {
            case NoteType.Don:
                if (e.Action is GameplayAction.RightCentre or GameplayAction.LeftCentre)
                    return UpdateResult(true);
                break;
            case NoteType.Kat:
                if (e.Action is GameplayAction.RightRim or GameplayAction.LeftRim)
                    return UpdateResult(true);
                break;
        }
        
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<GameplayAction> e)
    {
    }
}