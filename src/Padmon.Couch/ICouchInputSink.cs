namespace Padmon.Couch;

public interface ICouchInputSink
{
    void KeyDown(int virtualKey);

    void KeyUp(int virtualKey);

    void MouseButtonDown(int button);

    void MouseButtonUp(int button);

    void MoveMouse(int dx, int dy);

    void Scroll(int delta);

    void HideWindow();

    void OpenOnScreenKeyboard();
}
