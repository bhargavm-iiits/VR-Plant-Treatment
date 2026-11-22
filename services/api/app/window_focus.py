"""Bring CropScan XR's window to the front after Enter the field (Windows, laptop demo only).

Windows stops a background program from simply taking the focus: Unity asking for it only flashes its taskbar button,
and the browser keeps the screen. The documented way around that lock is to attach this thread's input to the
foreground window's thread for a moment, which lets SetForegroundWindow succeed; a single synthetic Alt key event is
the fallback when that is not enough. The target is found by process (Unity.exe on this project, or PlantDiseaseVR.exe).
"""

from __future__ import annotations

import logging
import os
import threading
import time
from pathlib import Path

log = logging.getLogger(__name__)

APP_EXES = {"unity.exe", "plantdiseasevr.exe"}

if os.name == "nt":
    import ctypes
    from ctypes import wintypes

    _user32 = ctypes.WinDLL("user32", use_last_error=True)
    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _EnumProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    _user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    _user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    _user32.GetForegroundWindow.restype = wintypes.HWND
    _user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    _user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    _user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    _user32.BringWindowToTop.argtypes = [wintypes.HWND]
    _user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    _user32.IsIconic.argtypes = [wintypes.HWND]
    _user32.IsWindowVisible.argtypes = [wintypes.HWND]
    _user32.GetWindow.restype = wintypes.HWND
    _user32.GetWindow.argtypes = [wintypes.HWND, ctypes.c_uint]
    _user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
    _kernel32.OpenProcess.restype = wintypes.HANDLE
    _kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    _kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    _kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

    _PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    _GW_OWNER = 4
    _SW_RESTORE, _SW_SHOW = 9, 5
    _VK_MENU, _KEYEVENTF_KEYUP = 0x12, 0x2


def _exe_of(pid: int) -> str:
    handle = _kernel32.OpenProcess(_PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return ""
    try:
        size = wintypes.DWORD(1024)
        buffer = ctypes.create_unicode_buffer(size.value)
        return buffer.value if _kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)) else ""
    finally:
        _kernel32.CloseHandle(handle)


def _title(hwnd) -> str:
    length = _user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    _user32.GetWindowTextW(hwnd, buffer, length + 1)
    return buffer.value


def find_app_window(project_name: str | None = None):
    """The main window of CropScan XR: the Unity Editor on this project, or the player. None when not open."""
    if os.name != "nt":
        return None
    found = []

    def visit(hwnd, _):
        if not _user32.IsWindowVisible(hwnd) or _user32.GetWindow(hwnd, _GW_OWNER):
            return True
        title = _title(hwnd)
        if not title:
            return True
        pid = wintypes.DWORD()
        _user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        exe = Path(_exe_of(pid.value)).name.lower()
        if exe not in APP_EXES:
            return True
        # The Editor's title starts with the project folder name ("BTP_Project - XRBootstrap - ... - Unity 6000...").
        # Never another project's Editor: a Unity window must name this project.
        if exe == "unity.exe" and (not project_name or project_name.lower() not in title.lower()):
            return True
        found.append((exe != "unity.exe", hwnd))   # prefer the Editor window (the player sorts after it)
        return True

    _user32.EnumWindows(_EnumProc(visit), 0)
    return sorted(found, key=lambda item: item[0])[0][1] if found else None


def bring_to_front(hwnd) -> bool:
    """Restores and focuses a window despite the foreground lock. True when it is now the foreground window."""
    if os.name != "nt" or not hwnd:
        return False
    if _user32.IsIconic(hwnd):
        _user32.ShowWindow(hwnd, _SW_RESTORE)
    foreground = _user32.GetForegroundWindow()
    if foreground == hwnd:
        return True
    this_thread = _kernel32.GetCurrentThreadId()
    foreground_thread = _user32.GetWindowThreadProcessId(foreground, None) if foreground else 0
    attached = bool(foreground_thread) and foreground_thread != this_thread and _user32.AttachThreadInput(this_thread, foreground_thread, True)
    try:
        _user32.ShowWindow(hwnd, _SW_SHOW)
        _user32.BringWindowToTop(hwnd)
        _user32.SetForegroundWindow(hwnd)
    finally:
        if attached:
            _user32.AttachThreadInput(this_thread, foreground_thread, False)
    if _user32.GetForegroundWindow() == hwnd:
        return True
    # Fallback: a synthetic Alt press counts as input, which releases the foreground lock.
    _user32.keybd_event(_VK_MENU, 0, 0, 0)
    _user32.keybd_event(_VK_MENU, 0, _KEYEVENTF_KEYUP, 0)
    _user32.SetForegroundWindow(hwnd)
    return _user32.GetForegroundWindow() == hwnd


def focus_app(project_name: str | None, wait_seconds: float = 0.0) -> None:
    """Brings CropScan XR to the front, now or (when it is still starting) as soon as its window appears.
    Runs on a background thread, so the web request returns immediately."""

    def run():
        deadline = time.monotonic() + wait_seconds
        while True:
            hwnd = find_app_window(project_name)
            if hwnd:
                # Unity redraws when it enters Play; a second nudge keeps it in front of the browser.
                ok = bring_to_front(hwnd)
                time.sleep(1.0)
                ok = bring_to_front(hwnd) or ok
                log.info("CropScan XR window %s", "brought to the front" if ok else "could not take the focus")
                return
            if time.monotonic() >= deadline:
                log.info("CropScan XR window not found; nothing to focus")
                return
            time.sleep(1.0)

    if os.name == "nt":
        threading.Thread(target=run, name="cropscan-xr-focus", daemon=True).start()
