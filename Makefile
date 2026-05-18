.PHONY: help publish stage deb-stage rpm deb pkg install install-deb uninstall uninstall-deb bump-patch bump-minor bump-major clean

NAME     := comfyui-server-manager
VERSION  := $(shell cat VERSION 2>/dev/null || echo 0.1.0)
PROJECT  := $(CURDIR)/ComfyUIServerManager/ComfyUIServerManager.csproj
PUBLISH  := $(CURDIR)/ComfyUIServerManager/bin/Release/net8.0/linux-x64/publish
STAGE    := $(CURDIR)/dist/stage/$(NAME)-$(VERSION)
TAR      := $(CURDIR)/dist/$(NAME)-$(VERSION).tar.gz
SPEC     := $(CURDIR)/packaging/$(NAME).spec
DESKTOP  := $(CURDIR)/packaging/$(NAME).desktop
ICO      := $(CURDIR)/ComfyUIServerManager/Comfy_Logo.ico
RPMROOT  := $(HOME)/rpmbuild
DEBSTAGE := $(CURDIR)/dist/deb-stage/$(NAME)_$(VERSION)
DEB      := $(CURDIR)/dist/$(NAME)_$(VERSION)-1_amd64.deb
ICON_SIZES := 32 48 64 128 256 512

help:
	@echo "$(NAME) — dev targets"
	@echo ""
	@echo "  make publish        dotnet publish -c Release -r linux-x64 (self-contained, single-file)"
	@echo "  make rpm            publish + stage + produce dist/*.rpm"
	@echo "  make deb            publish + stage + produce dist/*.deb"
	@echo "  make pkg            Build both rpm and deb"
	@echo "  make install        Install the latest RPM (Fedora)"
	@echo "  make install-deb    Install the latest .deb (Debian/Ubuntu)"
	@echo "  make uninstall      Remove the installed RPM"
	@echo "  make uninstall-deb  Remove the installed .deb"
	@echo "  make bump-patch / bump-minor / bump-major"
	@echo "  make clean          Remove dist/ and bin/Release/net8.0/linux-x64/"
	@echo ""
	@echo "Current version: $(VERSION)"

# No CEF here, so unlike the client-wrapper we *can* (and do) publish as a
# single-file binary. The Avalonia self-contained payload is ~70 MB.
publish:
	dotnet publish $(PROJECT) -c Release -r linux-x64 --self-contained \
		-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
		-p:Version=$(VERSION)
	@test -x $(PUBLISH)/ComfyUIServerManager && echo "Built: $(PUBLISH)/ComfyUIServerManager"

stage: publish
	@rm -rf $(STAGE)
	@mkdir -p $(STAGE)/opt/$(NAME) \
		$(STAGE)/share/applications \
		$(STAGE)/share/icons/hicolor/scalable/apps
	cp -r $(PUBLISH)/. $(STAGE)/opt/$(NAME)/
	install -Dm644 $(DESKTOP) $(STAGE)/share/applications/$(NAME).desktop
	@for s in $(ICON_SIZES); do \
		mkdir -p $(STAGE)/share/icons/hicolor/$${s}x$${s}/apps; \
		magick "$(ICO)[4]" -resize $${s}x$${s} -alpha on \
			$(STAGE)/share/icons/hicolor/$${s}x$${s}/apps/$(NAME).png 2>/dev/null \
			|| (echo "magick failed at size $$s — install ImageMagick"; exit 1); \
	done
	@cp $(SPEC) $(STAGE)/$(NAME).spec
	@echo "Staged to $(STAGE)"

rpm: stage
	@mkdir -p $(RPMROOT)/SOURCES $(RPMROOT)/SPECS $(CURDIR)/dist
	tar -C $(CURDIR)/dist/stage -czf $(TAR) $(NAME)-$(VERSION)
	cp $(TAR) $(RPMROOT)/SOURCES/
	cp $(SPEC) $(RPMROOT)/SPECS/$(NAME).spec
	rpmbuild --define "_version $(VERSION)" -ba $(RPMROOT)/SPECS/$(NAME).spec
	cp $(RPMROOT)/RPMS/x86_64/$(NAME)-$(VERSION)-*.rpm $(CURDIR)/dist/ 2>/dev/null || true
	cp $(RPMROOT)/SRPMS/$(NAME)-$(VERSION)-*.src.rpm $(CURDIR)/dist/ 2>/dev/null || true
	@echo ""
	@ls -la $(CURDIR)/dist/*.rpm

install:
	@RPM=$$(ls -t $(CURDIR)/dist/$(NAME)-*.x86_64.rpm 2>/dev/null | head -1); \
	if [ -z "$$RPM" ]; then echo "No RPM in dist/. Run 'make rpm' first."; exit 1; fi; \
	echo "Installing $$RPM"; \
	sudo dnf install -y "$$RPM"

uninstall:
	sudo dnf remove -y $(NAME) 2>&1 || true

deb-stage: publish
	@command -v dpkg-deb >/dev/null 2>&1 || { echo "dpkg-deb not found. Install: sudo dnf install -y dpkg"; exit 1; }
	@rm -rf $(DEBSTAGE)
	@mkdir -p $(DEBSTAGE)/opt/$(NAME) $(DEBSTAGE)/usr/bin \
		$(DEBSTAGE)/usr/share/applications \
		$(DEBSTAGE)/usr/share/icons/hicolor/scalable/apps $(DEBSTAGE)/DEBIAN
	cp -r $(PUBLISH)/. $(DEBSTAGE)/opt/$(NAME)/
	ln -sf /opt/$(NAME)/ComfyUIServerManager $(DEBSTAGE)/usr/bin/$(NAME)
	install -Dm644 $(DESKTOP) $(DEBSTAGE)/usr/share/applications/$(NAME).desktop
	@for s in $(ICON_SIZES); do \
		install -dm755 $(DEBSTAGE)/usr/share/icons/hicolor/$${s}x$${s}/apps; \
		magick "$(ICO)[4]" -resize $${s}x$${s} -alpha on \
			$(DEBSTAGE)/usr/share/icons/hicolor/$${s}x$${s}/apps/$(NAME).png 2>/dev/null \
			|| (echo "magick failed at size $$s — install ImageMagick"; exit 1); \
	done
	@sed "s/__VERSION__/$(VERSION)/g" $(CURDIR)/packaging/debian/control > $(DEBSTAGE)/DEBIAN/control
	@install -m 0755 $(CURDIR)/packaging/debian/postinst $(DEBSTAGE)/DEBIAN/postinst
	@install -m 0755 $(CURDIR)/packaging/debian/postrm $(DEBSTAGE)/DEBIAN/postrm
	@echo "Staged for deb at $(DEBSTAGE)"

deb: deb-stage
	@mkdir -p $(CURDIR)/dist
	fakeroot dpkg-deb --build --root-owner-group $(DEBSTAGE) $(DEB)
	@ls -la $(DEB)

pkg: rpm deb

install-deb:
	@DEB=$$(ls -t $(CURDIR)/dist/$(NAME)_*_amd64.deb 2>/dev/null | head -1); \
	if [ -z "$$DEB" ]; then echo "No .deb in dist/. Run 'make deb' first."; exit 1; fi; \
	echo "Installing $$DEB"; \
	sudo apt install -y "$$DEB"

uninstall-deb:
	sudo apt remove -y $(NAME) 2>&1 || true

bump-patch:
	@./scripts/bump-version.sh patch
bump-minor:
	@./scripts/bump-version.sh minor
bump-major:
	@./scripts/bump-version.sh major

clean:
	rm -rf $(CURDIR)/dist $(CURDIR)/ComfyUIServerManager/bin/Release/net8.0/linux-x64
