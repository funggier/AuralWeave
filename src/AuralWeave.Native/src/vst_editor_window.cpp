#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>

#include "auralweave/audio_engine_api.h"
#include "vst_editor_window.hpp"

#include <algorithm>
#include <iterator>
#include <stdexcept>

namespace auralweave {
namespace {

constexpr wchar_t frame_property[] = L"AuralWeave.VstEditorWindow";
constexpr int caption_height = 32;
constexpr int close_width = 44;
constexpr int close_id = 100;
constexpr int content_id = 101;

struct Palette {
    COLORREF background = RGB(23, 24, 26);
    COLORREF text = RGB(243, 244, 245);
    COLORREF border = RGB(52, 55, 59);
    COLORREF hover = RGB(68, 71, 75);
    COLORREF pressed = RGB(87, 91, 96);

    bool operator==(const Palette&) const noexcept = default;
};
thread_local Palette palette;

void fill(HDC dc, const RECT& rect, COLORREF color) noexcept {
    SetDCBrushColor(dc, color);
    FillRect(dc, &rect, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
}

BOOL CALLBACK redraw_editor(HWND window, LPARAM) noexcept {
    if (GetPropW(window, frame_property)) {
        InvalidateRect(window, nullptr, FALSE);
        if (const auto close = GetDlgItem(window, close_id)) InvalidateRect(close, nullptr, FALSE);
    }
    return TRUE;
}

} // namespace

void VstEditorWindow::create(HWND owner, const wchar_t* class_name, const wchar_t* title,
                             MessageHandler handler, void* context) {
    const auto instance = GetModuleHandleW(nullptr);
    WNDCLASSW info{};
    info.lpfnWndProc = window_proc;
    info.hInstance = instance;
    info.hCursor = LoadCursorW(nullptr, MAKEINTRESOURCEW(32512));
    info.lpszClassName = class_name;
    if (!RegisterClassW(&info) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        throw std::runtime_error("Cannot register the plugin editor window");

    handler_ = handler;
    context_ = context;
    window_ = CreateWindowExW(0, class_name, title, WS_OVERLAPPED | WS_CLIPCHILDREN,
                               CW_USEDEFAULT, CW_USEDEFAULT, width_, height_,
                               owner, nullptr, instance, this);
    if (!window_) throw std::runtime_error("Cannot create the plugin editor window");

    content_ = CreateWindowExW(0, L"STATIC", nullptr, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                                0, 0, 0, 0, window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(content_id)), instance, nullptr);
    close_button_ = CreateWindowExW(0, L"BUTTON", L"Close", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
                                     0, 0, 0, 0, window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(close_id)), instance, nullptr);
    if (!content_ || !close_button_ ||
        !SetWindowSubclass(close_button_, close_proc, 1, reinterpret_cast<DWORD_PTR>(this))) {
        destroy();
        throw std::runtime_error("Cannot create the plugin editor controls");
    }
    resize(width_, height_);
    layout();
}

void VstEditorWindow::destroy() noexcept {
    if (window_) DestroyWindow(window_);
}

void VstEditorWindow::show() noexcept {
    if (IsWindowVisible(window_)) {
        SetForegroundWindow(window_);
        return;
    }
    ShowWindow(window_, SW_SHOWNORMAL);
}

int VstEditorWindow::scale(int dips) const noexcept {
    return MulDiv(dips, static_cast<int>(GetDpiForWindow(window_)), USER_DEFAULT_SCREEN_DPI);
}

void VstEditorWindow::resize(int width, int height) noexcept {
    width_ = width;
    height_ = height;
    const auto edge = std::max(1, scale(1));
    SetWindowPos(window_, nullptr, 0, 0, width + 2 * edge, height + scale(caption_height) + edge,
                 SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
}

void VstEditorWindow::layout() const noexcept {
    const auto edge = std::max(1, scale(1));
    const auto caption = scale(caption_height);
    const auto close = std::min(width_, scale(close_width));
    if (close_button_) MoveWindow(close_button_, edge + width_ - close, edge, close, caption - edge, TRUE);
    if (content_) MoveWindow(content_, edge, caption, width_, height_, TRUE);
}

void VstEditorWindow::paint(HDC dc) const noexcept {
    RECT client{};
    GetClientRect(window_, &client);
    fill(dc, client, palette.background);
    SetDCBrushColor(dc, palette.border);
    FrameRect(dc, &client, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));

    wchar_t title[256]{};
    GetWindowTextW(window_, title, static_cast<int>(std::size(title)));
    RECT caption{scale(11), 0, client.right - scale(close_width), scale(caption_height)};
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, palette.text);
    const auto font = CreateFontW(-scale(13), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
                                  DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                  DEFAULT_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    const auto old_font = font ? SelectObject(dc, font) : nullptr;
    DrawTextW(dc, title, -1, &caption, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
    if (font) {
        SelectObject(dc, old_font);
        DeleteObject(font);
    }
}

void VstEditorWindow::paint_close(HDC dc, const RECT& rect, LRESULT state) const noexcept {
    const auto background = (state & BST_PUSHED) ? palette.pressed
        : (state & BST_HOT) ? palette.hover : palette.background;
    fill(dc, rect, background);
    const auto x = (rect.left + rect.right) / 2;
    const auto y = (rect.top + rect.bottom) / 2;
    const auto radius = scale(5);
    SetDCPenColor(dc, palette.text);
    const auto old_pen = SelectObject(dc, GetStockObject(DC_PEN));
    MoveToEx(dc, x - radius, y - radius, nullptr);
    LineTo(dc, x + radius + 1, y + radius + 1);
    MoveToEx(dc, x + radius, y - radius, nullptr);
    LineTo(dc, x - radius - 1, y + radius + 1);
    SelectObject(dc, old_pen);
    if (state & BST_FOCUS) DrawFocusRect(dc, &rect);
}

LRESULT CALLBACK VstEditorWindow::close_proc(HWND window, UINT message, WPARAM param, LPARAM data,
                                            UINT_PTR id, DWORD_PTR context) noexcept {
    switch (message) {
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            const auto* frame = reinterpret_cast<const VstEditorWindow*>(context);
            const auto state = SendMessageW(window, BM_GETSTATE, 0, 0);
            RECT rect{};
            GetClientRect(window, &rect);
            PAINTSTRUCT ps{};
            const auto dc = BeginPaint(window, &ps);
            frame->paint_close(dc, rect, state);
            EndPaint(window, &ps);
            return 0;
        }
        case WM_NCDESTROY:
            RemoveWindowSubclass(window, close_proc, id);
            break;
    }
    return DefSubclassProc(window, message, param, data);
}

LRESULT CALLBACK VstEditorWindow::window_proc(HWND window, UINT message, WPARAM param, LPARAM data) noexcept {
    if (message == WM_NCCREATE) {
        const auto* args = reinterpret_cast<const CREATESTRUCTW*>(data);
        auto* frame = static_cast<VstEditorWindow*>(args->lpCreateParams);
        if (!SetPropW(window, frame_property, frame)) return FALSE;
        frame->window_ = window;
    }
    auto* frame = static_cast<VstEditorWindow*>(GetPropW(window, frame_property));
    if (!frame) return DefWindowProcW(window, message, param, data);

    switch (message) {
        case WM_NCDESTROY:
            RemovePropW(window, frame_property);
            frame->window_ = frame->content_ = frame->close_button_ = nullptr;
            return DefWindowProcW(window, message, param, data);
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            PAINTSTRUCT ps{};
            const auto dc = BeginPaint(window, &ps);
            frame->paint(dc);
            EndPaint(window, &ps);
            return 0;
        }
        case WM_SIZE:
            frame->layout();
            return 0;
        case WM_DPICHANGED: {
            const auto* suggested = reinterpret_cast<const RECT*>(data);
            SetWindowPos(window, nullptr, suggested->left, suggested->top, 0, 0,
                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            frame->resize(frame->width_, frame->height_);
            frame->layout();
            return 0;
        }
        case WM_NCHITTEST: {
            POINT mouse{GET_X_LPARAM(data), GET_Y_LPARAM(data)};
            ScreenToClient(window, &mouse);
            RECT client{};
            GetClientRect(window, &client);
            if (PtInRect(&client, mouse) && mouse.y < frame->scale(caption_height) &&
                mouse.x < client.right - frame->scale(close_width))
                return HTCAPTION;
            break;
        }
        case WM_COMMAND:
            if (LOWORD(param) == close_id && HIWORD(param) == BN_CLICKED &&
                reinterpret_cast<HWND>(data) == frame->close_button_) {
                PostMessageW(window, WM_CLOSE, 0, 0);
                return 0;
            }
            break;
    }
    return frame->handler_(frame->context_, window, message, param, data);
}

} // namespace auralweave

AW_API void aw_vst_set_editor_palette(std::uint32_t background, std::uint32_t text, std::uint32_t border,
                                      std::uint32_t hover, std::uint32_t pressed) noexcept {
    const auralweave::Palette updated{background, text, border, hover, pressed};
    if (auralweave::palette == updated) return;
    auralweave::palette = updated;
    EnumThreadWindows(GetCurrentThreadId(), auralweave::redraw_editor, 0);
}
