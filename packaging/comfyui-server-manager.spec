# Pre-built self-contained .NET single-file binary — nothing for rpmbuild's
# auto-debuginfo scanner to work on. Turning it off also silences the
# "empty debugsource" error.
%global debug_package %{nil}
# .NET's single-file extractor ships native .so files inside the binary; we
# don't want rpmbuild stripping the published output or rewriting shebangs.
%global __strip /bin/true
%global __os_install_post %{nil}
%global _build_id_links none
%global __brp_mangle_shebangs %{nil}
AutoReq: no

Name:           comfyui-server-manager
Version:        %{?_version}%{!?_version:0.1.0}
Release:        1%{?dist}
Summary:        Tray utility to manage the ComfyUI server

License:        MIT
URL:            https://github.com/Draek2077/comfyui-server-manager
Source0:        %{name}-%{version}.tar.gz
BuildArch:      x86_64

# Avalonia on Linux uses X11 (XWayland under Wayland sessions). The
# StatusNotifierItem tray support is provided by the desktop environment;
# on stock GNOME a user-installed AppIndicator extension is required to
# render the tray icon, but the manager still works without it (the
# Settings / Logs windows still open).
Requires:       libX11
Requires:       libICE
Requires:       libSM
Requires:       fontconfig
Requires:       hicolor-icon-theme

%description
Avalonia tray application for managing a local ComfyUI server. Start, stop,
restart and view live logs from a system-tray icon; configure all of
ComfyUI's launch flags from a Settings panel. Part of the Draekz ComfyUI
suite alongside comfyui-client-wrapper and the ComfyUI Ultimate Installer.

%prep
%setup -q

%install
install -dm755 %{buildroot}/opt/%{name}
cp -r opt/%{name}/* %{buildroot}/opt/%{name}/

install -dm755 %{buildroot}%{_bindir}
ln -sf /opt/%{name}/ComfyUIServerManager %{buildroot}%{_bindir}/%{name}

install -Dm644 share/applications/%{name}.desktop \
    %{buildroot}%{_datadir}/applications/%{name}.desktop

for s in 32 48 64 128 256 512; do
    install -Dm644 share/icons/hicolor/${s}x${s}/apps/%{name}.png \
        %{buildroot}%{_datadir}/icons/hicolor/${s}x${s}/apps/%{name}.png
done

%files
/opt/%{name}
%{_bindir}/%{name}
%{_datadir}/applications/%{name}.desktop
%{_datadir}/icons/hicolor/*/apps/%{name}.*

%post
gtk-update-icon-cache -qf %{_datadir}/icons/hicolor &>/dev/null || :
update-desktop-database -q %{_datadir}/applications &>/dev/null || :

%postun
gtk-update-icon-cache -qf %{_datadir}/icons/hicolor &>/dev/null || :
update-desktop-database -q %{_datadir}/applications &>/dev/null || :

%changelog
* Wed May 13 2026 Philippe <draekz@gmail.com> - 1.0.0-1
- Initial Linux release: ported from WinForms (.NET 8) to Avalonia so the
  same codebase produces a Linux .rpm / .deb alongside the Windows build.
  Tray icon uses StatusNotifierItem so GNOME users will need an
  AppIndicator extension to see the tray icon, but the rest of the app
  works without one.
