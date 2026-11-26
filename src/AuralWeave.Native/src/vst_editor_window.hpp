#pragma once
#include <windows.h>

namespace auralweave {

// The top-level window owns the Studio chrome. Plugins receive only the
// child HWND, whose client rectangle is the size requested by the editor.
class VstEditorWindow {
public:
    using MessageHandler = LRESULT (*)(void*, HWND, UINT, WPARAM, LPARAM) noexcept;

    VstEditorWindow() = default;
    ~VstEditorWindow() { destroy(); }
    VstEditorWindow(const VstEditorWindow&) = delete;
    VstEditorWindow& operator=(const VstEditorWindow&) = delete;

    void create(HWND owner, const wchar_t* class_name, const wchar_t* title,
                MessageHandler handler, void* context);
    void destroy() noexcept;
    void show() noexcept;
    void resize(int width, int height) noexcept;
    [[nodiscard]] HWND window() const noexcept { return window_; }
    [[nodiscard]] HWND content() const noexcept { return content_; }

private:
    static LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM param, LPARAM data) noexcept;
    static LRESULT CALLBACK close_proc(HWND window, UINT message, WPARAM param, LPARAM data,
                                      UINT_PTR id, DWORD_PTR context) noexcept;
    int scale(int dips) const noexcept;
    void center_on_owner() const noexcept;
    [[nodiscard]] RECT close_bounds() const noexcept;
    void layout() const noexcept;
    void paint(HDC dc) const noexcept;
    void paint_close(HDC dc, const RECT& rect, UINT state) const noexcept;
    [[nodiscard]] bool is_close_click(HWND window, POINT point) const noexcept;
    void end_close_press(HWND window) noexcept;

    HWND window_{}, content_{}, close_button_{};
    MessageHandler handler_{};
    void* context_{};
    int width_{700}, height_{500};
    POINT close_press_{};
    bool close_pressed_{};
};

} // namespace auralweave
