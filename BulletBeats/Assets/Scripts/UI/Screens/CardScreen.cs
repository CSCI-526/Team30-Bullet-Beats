/// <summary>
/// A <see cref="UIScreen"/> that floats over the game as a card: the game behind it is shown
/// blurred and dimmed (<see cref="BlurBackdrop"/>) instead of the menu backdrop. Used by the
/// Level Completed / Game Over card and the Pause card.
/// </summary>
public abstract class CardScreen : UIScreen
{
    BlurBackdrop backdrop;

    protected override void Awake()
    {
        base.Awake();
        backdrop = BlurBackdrop.Create(Root);
    }

    public override void Open()
    {
        bool wasOpen = IsOpen;
        base.Open();
        if (wasOpen) return;
        // Grab the game first with this canvas hidden, then pop the card in over the blurred copy.
        // The coroutines run on this component, which sits on the always-active GameFlow object.
        Root.gameObject.SetActive(false);
        backdrop.Show(this, () =>
        {
            if (!IsOpen) return;
            Root.gameObject.SetActive(true);
            StartCoroutine(ScreenTransition.PopIn(Content));
        });
    }

    public override void Close()
    {
        base.Close();
        backdrop.Hide();
    }
}
