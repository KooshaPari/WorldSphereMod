FROM ubuntu:24.04

RUN dpkg --add-architecture i386 \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gettext-base \
        procps \
        xvfb \
        x11-utils \
        x11-xserver-utils \
        libvulkan1 \
        mesa-vulkan-drivers \
        vulkan-tools \
        wine \
        wine64 \
        winetricks \
        wine32 \
        cabextract \
        unzip \
        python3 \
        python3-pip \
        tini \
    && pip3 install --no-cache-dir requests \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /bin/bash wsm3d \
    && mkdir -p /opt/wsm3d-headless \
    && chown -R wsm3d:wsm3d /opt/wsm3d-headless

ENV WINEPREFIX=/wine \
    WINEARCH=win64 \
    WINEDEBUG=-all \
    WINEESYNC=1 \
    WINEDLLOVERRIDES=winemenubuilder.exe=d \
    DISPLAY=:99

COPY Tools/wsm3d-headless/entrypoint.sh /usr/local/bin/entrypoint.sh
COPY Tools/wsm3d-headless/goldberg/ /opt/wsm3d-headless/goldberg/

RUN chmod +x /usr/local/bin/entrypoint.sh

WORKDIR /work
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
