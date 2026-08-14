(() => {
  "use strict";

  const config = window.DFBLACKBOX_CONFIG;
  const sessionKey = "dfblackbox.portal.session";
  const nasSsoKey = "dfblackbox.portal.nas-sso";
  const initialQuery = new URLSearchParams(window.location.search);
  const claimCode = initialQuery.get("code")?.trim().toUpperCase() ?? "";
  const nasSsoResult = initialQuery.get("nas_sso")?.trim().toLowerCase() ?? "";
  let session = readSession();
  let organizations = [];
  let camerasById = new Map();
  let activeLive = null;
  let selectedCamera = null;
  let recordings = [];
  let recordingsByCamera = new Map();
  let selectedRecording = null;
  let recordingsNextCursor = null;
  let liveAttemptId = 0;
  let liveStartAbortController = null;
  let recordingsRequestId = 0;
  let presenceTimer = null;
  let pendingNasNotice = "";

  const elements = {
    notice: document.querySelector("#notice"),
    connectionBadge: document.querySelector("#connectionBadge"),
    signOutButton: document.querySelector("#signOutButton"),
    loginPanel: document.querySelector("#loginPanel"),
    loginForm: document.querySelector("#loginForm"),
    loginButton: document.querySelector("#loginButton"),
    emailInput: document.querySelector("#emailInput"),
    passwordInput: document.querySelector("#passwordInput"),
    approvalPanel: document.querySelector("#approvalPanel"),
    approvalForm: document.querySelector("#approvalForm"),
    claimStatePanel: document.querySelector("#claimStatePanel"),
    claimStateIcon: document.querySelector("#claimStateIcon"),
    claimStateTitle: document.querySelector("#claimStateTitle"),
    claimStateDescription: document.querySelector("#claimStateDescription"),
    claimCodeValue: document.querySelector("#claimCodeValue"),
    organizationSelect: document.querySelector("#organizationSelect"),
    siteSelect: document.querySelector("#siteSelect"),
    deviceNameInput: document.querySelector("#deviceNameInput"),
    approveButton: document.querySelector("#approveButton"),
    rejectButton: document.querySelector("#rejectButton"),
    portalPanel: document.querySelector("#portalPanel"),
    cameraGrid: document.querySelector("#cameraGrid"),
    cameraCardTemplate: document.querySelector("#cameraCardTemplate"),
    refreshButton: document.querySelector("#refreshButton"),
    recordingsPanel: document.querySelector("#recordingsPanel"),
    recordingsTitle: document.querySelector("#recordingsTitle"),
    recordingsDescription: document.querySelector("#recordingsDescription"),
    recordingsRefreshButton: document.querySelector("#recordingsRefreshButton"),
    recordingsLoading: document.querySelector("#recordingsLoading"),
    recordingsEmpty: document.querySelector("#recordingsEmpty"),
    recordingsList: document.querySelector("#recordingsList"),
    recordingsMoreButton: document.querySelector("#recordingsMoreButton"),
    recordingPlayerEmpty: document.querySelector("#recordingPlayerEmpty"),
    selectedRecordingName: document.querySelector("#selectedRecordingName"),
    selectedRecordingMeta: document.querySelector("#selectedRecordingMeta"),
    downloadRecordingButton: document.querySelector("#downloadRecordingButton"),
  };

  elements.loginForm.addEventListener("submit", signIn);
  elements.approvalForm.addEventListener("submit", approveClaim);
  elements.rejectButton.addEventListener("click", rejectClaim);
  elements.signOutButton.addEventListener("click", signOut);
  elements.refreshButton.addEventListener("click", loadPortal);
  elements.recordingsRefreshButton.addEventListener("click", () => loadRecordings(selectedCamera));
  elements.recordingsMoreButton.addEventListener("click", loadMoreRecordings);
  elements.downloadRecordingButton.addEventListener("click", downloadSelectedRecording);
  elements.organizationSelect.addEventListener("change", loadSites);
  window.addEventListener("pagehide", stopLiveForPageExit);
  elements.claimCodeValue.textContent = claimCode || "코드 없음";

  initialize();

  async function initialize() {
    if (!config?.supabaseUrl || !config?.publishableKey || !config?.functionBaseUrl) {
      showNotice("포털 연결 설정이 올바르지 않습니다.", "error");
      return;
    }

    if (!session || session.expiresAt <= Date.now()) {
      clearSession();
      showSignedOut();
      return;
    }

    try {
      await validateSession();
      if (!claimCode) {
        handleNasSsoReturn();
        if (nasSsoResult !== "error" && await ensureNasSessions()) return;
      }
      await showSignedIn();
      if (pendingNasNotice) showNotice(pendingNasNotice, "error");
    } catch {
      clearSession();
      showSignedOut();
      showNotice("로그인 세션이 만료되었습니다. 다시 로그인하세요.", "error");
    }
  }

  async function signIn(event) {
    event.preventDefault();
    setBusy(elements.loginButton, true, "로그인 중…");
    hideNotice();
    try {
      const response = await fetch(`${config.supabaseUrl}/auth/v1/token?grant_type=password`, {
        method: "POST",
        headers: apiHeaders(),
        body: JSON.stringify({
          email: elements.emailInput.value.trim(),
          password: elements.passwordInput.value,
        }),
      });
      const body = await readResponse(response);
      if (!response.ok || !body.access_token) throw new Error("이메일 또는 비밀번호를 확인하세요.");

      session = {
        accessToken: body.access_token,
        expiresAt: Date.now() + Number(body.expires_in ?? 3600) * 1000,
        email: body.user?.email ?? elements.emailInput.value.trim(),
      };
      sessionStorage.setItem(sessionKey, JSON.stringify(session));
      clearNasSsoState();
      elements.passwordInput.value = "";
      if (!claimCode && await ensureNasSessions(true)) return;
      await showSignedIn();
    } catch (error) {
      showNotice(error.message || "로그인에 실패했습니다.", "error");
    } finally {
      setBusy(elements.loginButton, false, "로그인");
    }
  }

  async function showSignedIn() {
    elements.loginPanel.hidden = true;
    elements.signOutButton.hidden = false;
    setConnection("관리자 로그인됨", "ready");
    if (claimCode) {
      elements.approvalPanel.hidden = false;
      elements.portalPanel.hidden = true;
      await loadClaimState();
    } else {
      elements.approvalPanel.hidden = true;
      elements.portalPanel.hidden = false;
      await loadPortal();
    }
  }

  async function loadClaimState() {
    elements.approvalForm.hidden = true;
    elements.claimStatePanel.hidden = false;
    setClaimState("neutral", "…", "등록 요청 확인 중", "서버에서 현재 요청 상태를 확인하고 있습니다.");
    try {
      const claim = await functionGet(`device-claims/${encodeURIComponent(claimCode)}`);
      switch (claim.status) {
        case "pending":
          elements.claimStatePanel.hidden = true;
          await loadOrganizations();
          elements.approvalForm.hidden = false;
          setConnection("승인 대기", "neutral");
          break;
        case "approved":
          setClaimState("ready", "✓", "이미 완료된 등록 요청입니다", "이 요청은 이미 승인됐습니다. DFBlackbox에서 등록 및 NAS 준비 결과를 확인하세요.");
          setConnection("승인 완료", "ready");
          break;
        case "rejected":
          setClaimState("error", "×", "거절된 등록 요청입니다", "관리자가 거절한 요청은 다시 사용할 수 없습니다. DFBlackbox에서 새 등록 요청을 만드세요.");
          setConnection("요청 거절됨", "neutral");
          break;
        case "expired":
          setClaimState("neutral", "!", "만료된 등록 요청입니다", "등록 유효 시간이 지났습니다. DFBlackbox에서 새 QR을 생성하세요.");
          setConnection("요청 만료됨", "neutral");
          break;
        default:
          setClaimState("error", "?", "유효하지 않은 등록 요청입니다", "등록코드를 확인하거나 DFBlackbox에서 새 QR을 생성하세요.");
          setConnection("요청 없음", "neutral");
          break;
      }
    } catch (error) {
      setClaimState("error", "!", "등록 요청을 확인하지 못했습니다", error.message || "잠시 후 다시 시도하세요.");
      setConnection("확인 실패", "neutral");
    }
  }

  function showSignedOut() {
    clearPresenceTimer();
    stopActiveLive().catch(() => undefined);
    closeRecordings();
    elements.loginPanel.hidden = false;
    elements.approvalPanel.hidden = true;
    elements.portalPanel.hidden = true;
    elements.signOutButton.hidden = true;
    setConnection("로그인 필요", "neutral");
  }

  async function loadOrganizations() {
    organizations = await rest("organizations?select=id,name&order=name");
    elements.organizationSelect.replaceChildren();
    for (const organization of organizations) {
      elements.organizationSelect.add(new Option(organization.name, organization.id));
    }
    if (!organizations.length) throw new Error("승인 가능한 조직이 없습니다.");
    await loadSites();
  }

  async function loadSites() {
    const organizationId = elements.organizationSelect.value;
    elements.siteSelect.replaceChildren();
    if (!organizationId) return;
    try {
      const sites = await rest(`sites?select=id,name&organization_id=eq.${encodeURIComponent(organizationId)}&order=name`);
      for (const site of sites) elements.siteSelect.add(new Option(site.name, site.id));
      if (!sites.length) elements.siteSelect.add(new Option("설치 장소 없음", ""));
    } catch (error) {
      showNotice(error.message, "error");
    }
  }

  async function approveClaim(event) {
    event.preventDefault();
    if (!claimCode) return showNotice("등록코드가 없습니다.", "error");
    if (!elements.organizationSelect.value || !elements.siteSelect.value) {
      return showNotice("조직과 설치 장소를 선택하세요.", "error");
    }

    setBusy(elements.approveButton, true, "승인 중…");
    elements.rejectButton.disabled = true;
    try {
      await functionRequest(`device-claims/${encodeURIComponent(claimCode)}/approve`, {
        organization_id: elements.organizationSelect.value,
        site_id: elements.siteSelect.value,
        device_name: elements.deviceNameInput.value.trim(),
      });
      showNotice("승인이 완료되었습니다. DFBlackbox가 NAS 저장소를 준비하고 있습니다.", "success");
      elements.approvalForm.hidden = true;
      elements.claimStatePanel.hidden = false;
      setClaimState("ready", "✓", "기기 승인이 완료되었습니다", "DFBlackbox가 승인 결과를 확인하고 NAS 저장소를 준비하고 있습니다.");
      setConnection("승인 완료", "ready");
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      setBusy(elements.approveButton, false, "이 기기 승인");
      elements.rejectButton.disabled = false;
    }
  }

  async function rejectClaim() {
    if (!claimCode || !elements.organizationSelect.value) {
      return showNotice("등록 요청과 조직을 확인하세요.", "error");
    }
    if (!window.confirm("이 기기 등록 요청을 거절할까요?")) return;

    setBusy(elements.rejectButton, true, "거절 중…");
    elements.approveButton.disabled = true;
    try {
      await functionRequest(`device-claims/${encodeURIComponent(claimCode)}/reject`, {
        organization_id: elements.organizationSelect.value,
      });
      showNotice("등록 요청을 거절했습니다.", "success");
      elements.approvalForm.hidden = true;
      elements.claimStatePanel.hidden = false;
      setClaimState("error", "×", "등록 요청을 거절했습니다", "이 요청은 다시 승인할 수 없습니다. 새 등록이 필요하면 DFBlackbox에서 다시 시작하세요.");
      setConnection("요청 거절됨", "neutral");
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      setBusy(elements.rejectButton, false, "거절");
      elements.approveButton.disabled = false;
    }
  }

  async function loadPortal() {
    await stopActiveLive();
    clearPresenceTimer();
    setBusy(elements.refreshButton, true, "불러오는 중…");
    try {
      const result = await serviceRequest(config.recordingMediaBaseUrl, "portal/cameras", { method: "GET" });
      const cameras = Array.isArray(result.items) ? result.items : [];
      renderCameras(cameras);
      recordingsByCamera = new Map();
      const recordingResults = await Promise.allSettled(cameras.map(async (camera) => {
        const page = await fetchRecordingPage(camera.id);
        recordingsByCamera.set(camera.id, page);
        updateRecordingCount(camera.id, page.items.length);
      }));
      if (recordingResults.some((item) => item.status === "rejected")) {
        showNotice("일부 카메라의 녹화 목록을 불러오지 못했습니다. 목록 새로고침을 사용하세요.", "error");
      }
      if (selectedCamera && recordingsByCamera.has(selectedCamera.id)) {
        useCachedRecordings(selectedCamera.id);
      }
      schedulePresenceRefresh(cameras);
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      setBusy(elements.refreshButton, false, "새로고침");
    }
  }

  function renderCameras(cameras) {
    camerasById = new Map(cameras.map((camera) => [camera.id, camera]));
    elements.cameraGrid.replaceChildren();
    if (!cameras.length) {
      const empty = document.createElement("div");
      empty.className = "empty-state";
      empty.innerHTML = "<strong>등록된 카메라가 없습니다.</strong><span>DFBlackbox에서 기기 등록을 시작하세요.</span>";
      elements.cameraGrid.append(empty);
      closeRecordings();
      return;
    }
    for (const camera of cameras) {
      const card = elements.cameraCardTemplate.content.cloneNode(true);
      const article = card.querySelector(".camera-card");
      article.dataset.cameraId = camera.id;
      applyCameraState(article, camera);
      card.querySelector('[data-action="live-start"]').addEventListener("click", () => startLive(camera, article));
      card.querySelector('[data-action="live-stop"]').addEventListener("click", stopActiveLive);
      card.querySelector('[data-action="recordings"]').addEventListener("click", () => openRecordings(camera));
      elements.cameraGrid.append(card);
    }

    if (selectedCamera) {
      selectedCamera = camerasById.get(selectedCamera.id) ?? null;
      if (!selectedCamera) closeRecordings();
    }
  }

  function applyCameraState(article, camera) {
    article.querySelector('[data-field="name"]').textContent = camera.display_name;
    article.querySelector('[data-field="type"]').textContent = camera.camera_type;
    article.querySelector('[data-field="connection"]').textContent = camera.device_online
      ? `온라인 · ${statusText(camera.connection_state)}`
      : "오프라인";
    article.querySelector('[data-field="storage"]').textContent = statusText(camera.storage_state);
    article.querySelector('[data-field="ip"]').textContent = camera.device_public_ip || "확인되지 않음";
    article.querySelector('[data-field="last-seen"]').textContent = formatLastSeen(camera.device_last_seen_at);
  }

  async function fetchRecordingPage(cameraId, cursor = null) {
    const cursorQuery = cursor ? `&cursor=${encodeURIComponent(cursor)}` : "";
    const result = await serviceRequest(
      config.recordingMediaBaseUrl,
      `cameras/${encodeURIComponent(cameraId)}/recordings?limit=50${cursorQuery}`,
      { method: "GET" },
    );
    return {
      items: Array.isArray(result.items) ? result.items : [],
      nextCursor: typeof result.next_cursor === "string" && result.next_cursor ? result.next_cursor : null,
    };
  }

  function useCachedRecordings(cameraId) {
    const cached = recordingsByCamera.get(cameraId) ?? { items: [], nextCursor: null };
    recordings = [...cached.items];
    recordingsNextCursor = cached.nextCursor;
    clearRecordingPlayer();
    renderRecordings();
    elements.recordingsLoading.hidden = true;
    elements.recordingsMoreButton.hidden = !recordingsNextCursor;
  }

  function updateRecordingCount(cameraId, count) {
    const article = [...elements.cameraGrid.querySelectorAll(".camera-card")]
      .find((item) => item.dataset.cameraId === cameraId);
    const button = article?.querySelector('[data-action="recordings"]');
    if (button) button.textContent = `녹화 다운로드 (${count})`;
  }

  function schedulePresenceRefresh(cameras = [...camerasById.values()]) {
    clearPresenceTimer();
    if (!session || !cameras.length) return;
    const intervalSeconds = Math.min(...cameras.map((camera) => {
      const heartbeat = Number(camera.heartbeat_interval_seconds ?? 3);
      return Math.min(Math.max(heartbeat * 3, 10), 60);
    }));
    presenceTimer = window.setTimeout(refreshCameraPresence, intervalSeconds * 1000);
  }

  async function refreshCameraPresence() {
    presenceTimer = null;
    try {
      const result = await serviceRequest(config.recordingMediaBaseUrl, "portal/cameras", { method: "GET" });
      const cameras = Array.isArray(result.items) ? result.items : [];
      camerasById = new Map(cameras.map((camera) => [camera.id, camera]));
      for (const camera of cameras) {
        const article = [...elements.cameraGrid.querySelectorAll(".camera-card")]
          .find((item) => item.dataset.cameraId === camera.id);
        if (article) applyCameraState(article, camera);
      }
      if (selectedCamera) selectedCamera = camerasById.get(selectedCamera.id) ?? null;
      schedulePresenceRefresh(cameras);
    } catch {
      schedulePresenceRefresh();
    }
  }

  function clearPresenceTimer() {
    if (presenceTimer !== null) {
      clearTimeout(presenceTimer);
      presenceTimer = null;
    }
  }

  function formatLastSeen(value) {
    const date = new Date(value ?? "");
    if (!Number.isFinite(date.getTime())) return "접속 기록 없음";
    return new Intl.DateTimeFormat("ko-KR", {
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    }).format(date);
  }

  async function startLive(camera, card) {
    const livekit = window.LivekitClient;
    const startButton = card.querySelector('[data-action="live-start"]');
    const stopButton = card.querySelector('[data-action="live-stop"]');
    const video = card.querySelector('[data-field="live-video"]');
    const placeholder = card.querySelector('[data-field="live-placeholder"]');
    const status = card.querySelector('[data-field="live-status"]');

    if (!livekit?.Room || !livekit?.RoomEvent || !livekit?.Track) {
      showNotice("실시간 재생 모듈을 불러오지 못했습니다. 네트워크 연결 후 페이지를 새로고침하세요.", "error");
      return;
    }
    if (!config.mediaSessionBaseUrl) {
      showNotice("실시간 재생 서버 설정이 없습니다.", "error");
      return;
    }

    await stopActiveLive();
    const attemptId = ++liveAttemptId;
    const startAbortController = new AbortController();
    liveStartAbortController = startAbortController;
    hideNotice();
    setBusy(startButton, true, "연결 준비 중…");
    setLiveStatus(status, "연결 준비 중", "connecting");

    let room = null;
    try {
      const credentials = await mediaRequest(
        config.mediaSessionBaseUrl,
        `cameras/${encodeURIComponent(camera.id)}/stream-session`,
        {},
        { signal: startAbortController.signal },
      );
      if (!credentials.livekit_url || !credentials.participant_token) {
        throw new Error("실시간 연결 정보가 올바르지 않습니다.");
      }
      if (attemptId !== liveAttemptId) {
        resetLiveCard({ video, placeholder, status, startButton, stopButton }, "중지됨", "neutral");
        return;
      }

      room = new livekit.Room({ adaptiveStream: true });
      const live = {
        camera,
        card,
        room,
        video,
        placeholder,
        status,
        startButton,
        stopButton,
        heartbeatTimer: null,
        heartbeatAbortController: null,
        leaseUntilMs: parseLeaseUntil(credentials.lease_until),
        stopped: false,
        videoTrack: null,
      };
      activeLive = live;
      bindLiveEvents(live, livekit);
      await room.prepareConnection(credentials.livekit_url, credentials.participant_token);
      if (attemptId !== liveAttemptId || activeLive !== live) {
        await room.disconnect().catch(() => undefined);
        return;
      }
      await room.connect(credentials.livekit_url, credentials.participant_token, { autoSubscribe: true });
      if (activeLive !== live || live.stopped) return;

      setLiveStatus(status, "카메라 송출 대기 중", "connecting");
      startButton.hidden = true;
      startButton.disabled = false;
      startButton.textContent = "실시간 보기";
      stopButton.hidden = false;
      scheduleLiveHeartbeat(live, 25_000);
    } catch (error) {
      if (room) await room.disconnect().catch(() => undefined);
      if (activeLive?.room === room) activeLive = null;
      if (attemptId !== liveAttemptId) {
        resetLiveCard({ video, placeholder, status, startButton, stopButton }, "중지됨", "neutral");
        return;
      }
      resetLiveCard({ video, placeholder, status, startButton, stopButton }, "연결 실패", "error");
      showNotice("실시간 영상에 연결하지 못했습니다. 잠시 후 다시 시도하세요.", "error");
    } finally {
      if (liveStartAbortController === startAbortController) {
        liveStartAbortController = null;
      }
    }
  }

  function bindLiveEvents(live, livekit) {
    live.room
      .on(livekit.RoomEvent.TrackSubscribed, (track) => {
        if (activeLive !== live || track.kind !== livekit.Track.Kind.Video) return;
        live.videoTrack?.detach(live.video);
        live.videoTrack = track;
        track.attach(live.video);
        live.video.hidden = false;
        live.placeholder.hidden = true;
        live.video.play().catch(() => undefined);
        setLiveStatus(live.status, "실시간", "ready");
        setConnection("실시간 연결됨", "ready");
      })
      .on(livekit.RoomEvent.TrackUnsubscribed, (track) => {
        if (activeLive !== live || track !== live.videoTrack) return;
        track.detach(live.video);
        live.videoTrack = null;
        live.video.hidden = true;
        live.placeholder.hidden = false;
        setLiveStatus(live.status, "송출 재연결 대기", "connecting");
        setConnection("카메라 송출 대기", "neutral");
      })
      .on(livekit.RoomEvent.Reconnecting, () => {
        if (activeLive === live) setLiveStatus(live.status, "재연결 중", "connecting");
      })
      .on(livekit.RoomEvent.Reconnected, () => {
        if (activeLive === live) {
          setLiveStatus(live.status, live.videoTrack ? "실시간" : "카메라 송출 대기 중", live.videoTrack ? "ready" : "connecting");
        }
      })
      .on(livekit.RoomEvent.TrackSubscriptionFailed, () => {
        if (activeLive === live) setLiveStatus(live.status, "영상 구독 실패", "error");
      })
      .on(livekit.RoomEvent.Disconnected, () => {
        if (activeLive !== live || live.stopped) return;
        clearTimeout(live.heartbeatTimer);
        live.heartbeatAbortController?.abort();
        live.heartbeatAbortController = null;
        live.stopped = true;
        activeLive = null;
        resetLiveCard(live, "연결 종료됨", "error");
        setConnection("관리자 로그인됨", "ready");
      });
  }

  function scheduleLiveHeartbeat(live, delay) {
    clearTimeout(live.heartbeatTimer);
    live.heartbeatTimer = window.setTimeout(async () => {
      if (activeLive !== live || live.stopped) return;
      if (Date.now() >= live.leaseUntilMs) {
        await stopLiveAfterLeaseFailure(live);
        return;
      }
      const heartbeatAbortController = new AbortController();
      live.heartbeatAbortController = heartbeatAbortController;
      try {
        const heartbeat = await mediaRequest(
          config.mediaSessionBaseUrl,
          `cameras/${encodeURIComponent(live.camera.id)}/stream-heartbeat`,
          {},
          { signal: heartbeatAbortController.signal },
        );
        if (activeLive !== live || live.stopped) return;
        live.leaseUntilMs = parseLeaseUntil(heartbeat.lease_until, live.leaseUntilMs);
        setLiveStatus(
          live.status,
          live.videoTrack ? "실시간" : "카메라 송출 대기 중",
          live.videoTrack ? "ready" : "connecting",
        );
      } catch (error) {
        if (heartbeatAbortController.signal.aborted || activeLive !== live || live.stopped) return;
        if (Date.now() >= live.leaseUntilMs) {
          await stopLiveAfterLeaseFailure(live);
          return;
        }
        setLiveStatus(live.status, "연결 유지 재시도 중", "connecting");
        showNotice(error.message || "실시간 보기 유지 요청에 실패했습니다.", "error");
      } finally {
        if (live.heartbeatAbortController === heartbeatAbortController) {
          live.heartbeatAbortController = null;
        }
        if (activeLive === live && !live.stopped) {
          scheduleLiveHeartbeat(live, nextHeartbeatDelay(live));
        }
      }
    }, delay);
  }

  async function stopActiveLive() {
    liveAttemptId += 1;
    liveStartAbortController?.abort();
    liveStartAbortController = null;
    const live = activeLive;
    if (!live) return;
    activeLive = null;
    live.stopped = true;
    clearTimeout(live.heartbeatTimer);
    live.heartbeatAbortController?.abort();
    live.heartbeatAbortController = null;
    live.videoTrack?.detach(live.video);
    await live.room.disconnect().catch(() => undefined);
    resetLiveCard(live, "중지됨", "neutral");
    if (session) setConnection("관리자 로그인됨", "ready");
  }

  function stopLiveForPageExit() {
    clearPresenceTimer();
    liveAttemptId += 1;
    liveStartAbortController?.abort();
    liveStartAbortController = null;
    if (!activeLive) return;
    activeLive.stopped = true;
    clearTimeout(activeLive.heartbeatTimer);
    activeLive.heartbeatAbortController?.abort();
    activeLive.heartbeatAbortController = null;
    activeLive.room.disconnect().catch(() => undefined);
    activeLive = null;
  }

  async function stopLiveAfterLeaseFailure(live) {
    if (activeLive !== live || live.stopped) return;
    liveAttemptId += 1;
    activeLive = null;
    live.stopped = true;
    clearTimeout(live.heartbeatTimer);
    live.heartbeatAbortController?.abort();
    live.heartbeatAbortController = null;
    live.videoTrack?.detach(live.video);
    await live.room.disconnect().catch(() => undefined);
    resetLiveCard(live, "연결 유지 실패", "error");
    setConnection("관리자 로그인됨", "ready");
    showNotice("실시간 보기 유지 시간이 만료되었습니다. 다시 연결하세요.", "error");
  }

  function parseLeaseUntil(value, fallback = Date.now() + 90_000) {
    const parsed = Date.parse(value ?? "");
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  function nextHeartbeatDelay(live) {
    return Math.max(0, Math.min(25_000, live.leaseUntilMs - Date.now()));
  }

  function resetLiveCard(live, label, state) {
    live.videoTrack?.detach(live.video);
    live.video.pause();
    live.video.srcObject = null;
    live.video.hidden = true;
    live.placeholder.hidden = false;
    live.startButton.hidden = false;
    live.startButton.disabled = false;
    live.startButton.textContent = "실시간 보기";
    live.stopButton.hidden = true;
    setLiveStatus(live.status, label, state);
  }

  function setLiveStatus(element, label, state) {
    element.textContent = label;
    element.className = state === "neutral" ? "live-status" : `live-status state-${state}`;
  }

  async function openRecordings(camera) {
    selectedCamera = camera;
    elements.recordingsPanel.hidden = false;
    elements.recordingsTitle.textContent = `${camera.display_name} 녹화 다운로드`;
    elements.recordingsDescription.textContent = "PC가 오프라인이어도 NAS가 접속 가능하면 등록된 녹화 파일을 다운로드할 수 있습니다.";
    elements.recordingsPanel.scrollIntoView({ behavior: "smooth", block: "start" });
    if (recordingsByCamera.has(camera.id)) {
      useCachedRecordings(camera.id);
      return;
    }
    await loadRecordings(camera);
  }

  async function loadRecordings(camera, options = {}) {
    if (!camera || selectedCamera?.id !== camera.id) return;
    if (!config.recordingMediaBaseUrl) {
      showNotice("녹화영상 서버 설정이 없습니다.", "error");
      return;
    }

    const append = Boolean(options.append);
    const cursor = options.cursor ?? null;
    const cameraId = camera.id;
    const requestId = ++recordingsRequestId;
    elements.recordingsLoading.hidden = append;
    elements.recordingsEmpty.hidden = true;
    elements.recordingsMoreButton.hidden = true;
    setBusy(elements.recordingsRefreshButton, true, "불러오는 중…");
    if (append) setBusy(elements.recordingsMoreButton, true, "불러오는 중…");
    if (!append) {
      recordings = [];
      recordingsNextCursor = null;
      elements.recordingsList.replaceChildren();
      clearRecordingPlayer();
    }

    try {
      const result = await fetchRecordingPage(cameraId, cursor);
      if (selectedCamera?.id !== cameraId || requestId !== recordingsRequestId) return;

      const items = result.items;
      recordings = append ? [...recordings, ...items] : items;
      recordingsNextCursor = result.nextCursor;
      recordingsByCamera.set(cameraId, { items: recordings, nextCursor: recordingsNextCursor });
      updateRecordingCount(cameraId, recordings.length);
      renderRecordings();
    } catch (error) {
      if (selectedCamera?.id !== cameraId || requestId !== recordingsRequestId) return;
      showNotice(error.message || "녹화영상 목록을 불러오지 못했습니다.", "error");
      if (!append) elements.recordingsEmpty.hidden = false;
    } finally {
      if (selectedCamera?.id === cameraId && requestId === recordingsRequestId) {
        elements.recordingsLoading.hidden = true;
        elements.recordingsMoreButton.hidden = !recordingsNextCursor;
        setBusy(elements.recordingsMoreButton, false, "이전 영상 더 보기");
        setBusy(elements.recordingsRefreshButton, false, "목록 새로고침");
      }
    }
  }

  async function loadMoreRecordings() {
    if (!selectedCamera || !recordingsNextCursor) return;
    await loadRecordings(selectedCamera, { append: true, cursor: recordingsNextCursor });
  }

  function renderRecordings() {
    elements.recordingsList.replaceChildren();
    elements.recordingsEmpty.hidden = recordings.length > 0;
    for (const recording of recordings) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "recording-item";
      button.dataset.recordingId = recording.id;
      button.setAttribute("aria-current", recording.id === selectedRecording?.id ? "true" : "false");

      const name = document.createElement("span");
      name.className = "recording-item-name";
      name.textContent = recording.original_file_name || "녹화영상.mp4";
      const state = document.createElement("span");
      state.className = "recording-item-state";
      state.textContent = "다운로드";
      const meta = document.createElement("span");
      meta.className = "recording-item-meta";
      meta.textContent = recordingMeta(recording);
      button.append(name, state, meta);
      button.addEventListener("click", () => selectRecording(recording));
      elements.recordingsList.append(button);
    }
  }

  function selectRecording(recording) {
    selectedRecording = recording;
    for (const item of elements.recordingsList.querySelectorAll(".recording-item")) {
      item.setAttribute("aria-current", item.dataset.recordingId === recording.id ? "true" : "false");
    }
    elements.recordingPlayerEmpty.hidden = false;
    elements.recordingPlayerEmpty.querySelector("strong").textContent = "다운로드 준비됨";
    elements.selectedRecordingName.textContent = recording.original_file_name || "녹화영상.mp4";
    elements.selectedRecordingMeta.textContent = recordingMeta(recording);
    elements.downloadRecordingButton.disabled = false;
  }

  async function downloadSelectedRecording() {
    const recording = selectedRecording;
    if (!recording) return;
    setBusy(elements.downloadRecordingButton, true, "다운로드 준비 중…");
    try {
      const result = await serviceRequest(
        config.recordingMediaBaseUrl,
        `recordings/${encodeURIComponent(recording.id)}/download-url`,
        { method: "POST" },
      );
      if (selectedRecording?.id !== recording.id) return;
      const downloadUrl = requireNasDownloadUrl(result.download_url);
      window.location.assign(downloadUrl);
      showNotice("NAS에서 녹화영상 다운로드를 시작했습니다.", "success");
    } catch (error) {
      showNotice(error.message || "녹화영상 다운로드를 준비하지 못했습니다.", "error");
    } finally {
      if (selectedRecording?.id === recording.id) {
        setBusy(elements.downloadRecordingButton, false, "선택 영상 다운로드");
      }
    }
  }

  function closeRecordings() {
    recordingsRequestId += 1;
    selectedCamera = null;
    recordings = [];
    recordingsNextCursor = null;
    elements.recordingsPanel.hidden = true;
    elements.recordingsList.replaceChildren();
    clearRecordingPlayer();
  }

  function clearRecordingPlayer() {
    selectedRecording = null;
    elements.recordingPlayerEmpty.hidden = false;
    elements.recordingPlayerEmpty.querySelector("strong").textContent = "다운로드할 영상을 선택하세요.";
    elements.selectedRecordingName.textContent = "선택된 영상 없음";
    elements.selectedRecordingMeta.textContent = "";
    elements.downloadRecordingButton.disabled = true;
    elements.downloadRecordingButton.textContent = "선택 영상 다운로드";
  }

  function recordingMeta(recording) {
    return [
      formatDateTime(recording.recorded_at),
      formatDuration(recording.duration_seconds),
      formatBytes(recording.file_size_bytes),
    ].filter(Boolean).join(" · ");
  }

  function requireNasDownloadUrl(value) {
    let url;
    try {
      url = new URL(value);
    } catch {
      throw new Error("다운로드 주소가 올바르지 않습니다.");
    }
    if (url.protocol !== "https:" || url.username || url.password) {
      throw new Error("다운로드 주소가 올바르지 않습니다.");
    }
    return url.href;
  }

  function formatDateTime(value) {
    const date = new Date(value);
    if (!value || Number.isNaN(date.getTime())) return "촬영 시각 미상";
    return new Intl.DateTimeFormat("ko-KR", {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    }).format(date);
  }

  function formatDuration(value) {
    const totalSeconds = Math.max(0, Math.round(Number(value)));
    if (!Number.isFinite(totalSeconds) || totalSeconds === 0) return "길이 미상";
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    return hours > 0
      ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
      : `${minutes}:${String(seconds).padStart(2, "0")}`;
  }

  function formatBytes(value) {
    if (value === null || value === undefined || value === "") return "크기 미상";
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes < 0) return "크기 미상";
    if (bytes < 1024) return `${bytes} B`;
    const units = ["KB", "MB", "GB", "TB"];
    const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length);
    const size = bytes / (1024 ** exponent);
    return `${size >= 10 ? size.toFixed(0) : size.toFixed(1)} ${units[exponent - 1]}`;
  }

  async function validateSession() {
    const response = await fetch(`${config.supabaseUrl}/auth/v1/user`, {
      headers: authenticatedHeaders(),
    });
    if (!response.ok) throw new Error("invalid_session");
  }

  async function signOut() {
    await stopActiveLive();
    closeRecordings();
    if (session?.accessToken) {
      await fetch(`${config.supabaseUrl}/auth/v1/logout?scope=local`, {
        method: "POST",
        headers: authenticatedHeaders(),
      }).catch(() => undefined);
    }
    const logoutUrl = firstNasLogoutUrl();
    clearSession();
    clearNasSsoState();
    hideNotice();
    if (logoutUrl) {
      const url = new URL(logoutUrl);
      url.searchParams.set("return_to", portalReturnUrl());
      window.location.assign(url.toString());
      return;
    }
    showSignedOut();
  }

  function handleNasSsoReturn() {
    if (!nasSsoResult) return;
    const url = new URL(window.location.href);
    url.searchParams.delete("nas_sso");
    window.history.replaceState({}, "", url.toString());

    const state = readNasSsoState();
    if (nasSsoResult === "ready" && state && Number.isInteger(state.pendingIndex)) {
      const current = state.sessions?.[state.pendingIndex];
      if (current) {
        current.ready = true;
        current.assertion = "";
        state.pendingIndex = null;
        writeNasSsoState(state);
      }
      return;
    }
    if (nasSsoResult === "error") {
      clearNasSsoState();
      pendingNasNotice = "NAS 다운로드 로그인을 연결하지 못했습니다. 새로고침하여 다시 시도하세요.";
    }
  }

  async function ensureNasSessions(force = false) {
    let state = readNasSsoState();
    const now = Date.now();
    if (!force && state?.sessions?.length
        && state.sessions.every((item) => item.ready)
        && Number(state.expiresAt) > now + 60_000) {
      return false;
    }

    if (!force && state?.sessions?.length) {
      const pendingIndex = state.sessions.findIndex((item) => !item.ready && item.assertion);
      if (pendingIndex >= 0) {
        return submitNasExchange(state, pendingIndex);
      }
    }

    const result = await serviceRequest(config.recordingMediaBaseUrl, "nas-session", {
      method: "POST",
      body: "{}",
    });
    const sessions = Array.isArray(result.sessions) ? result.sessions.map(normalizeNasSession) : [];
    if (!sessions.length) {
      clearNasSsoState();
      return false;
    }
    state = {
      sessions,
      pendingIndex: null,
      expiresAt: Math.min(...sessions.map((item) => item.expiresAt)),
    };
    writeNasSsoState(state);
    return submitNasExchange(state, 0);
  }

  function normalizeNasSession(value) {
    const exchangeUrl = requireNasGatewayUrl(value?.exchange_url, "auth.php");
    const logoutUrl = requireNasGatewayUrl(value?.logout_url, "logout.php");
    const assertion = typeof value?.assertion === "string" ? value.assertion : "";
    const expiresAt = Date.parse(value?.session_expires_at ?? "");
    if (!assertion || assertion.length > 16_384 || !Number.isFinite(expiresAt)) {
      throw new Error("NAS 로그인 응답이 올바르지 않습니다.");
    }
    return { exchangeUrl, logoutUrl, assertion, expiresAt, ready: false };
  }

  function requireNasGatewayUrl(value, fileName) {
    const url = new URL(String(value ?? ""));
    if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash
        || !url.pathname.endsWith(`/${fileName}`)) {
      throw new Error("NAS 로그인 주소가 올바르지 않습니다.");
    }
    return url.toString();
  }

  function submitNasExchange(state, index) {
    const target = state.sessions[index];
    if (!target?.assertion || target.expiresAt <= Date.now()) {
      clearNasSsoState();
      return false;
    }
    state.pendingIndex = index;
    writeNasSsoState(state);
    const form = document.createElement("form");
    form.method = "POST";
    form.action = target.exchangeUrl;
    form.hidden = true;
    const assertion = document.createElement("input");
    assertion.type = "hidden";
    assertion.name = "assertion";
    assertion.value = target.assertion;
    const returnTo = document.createElement("input");
    returnTo.type = "hidden";
    returnTo.name = "return_to";
    returnTo.value = portalReturnUrl();
    form.append(assertion, returnTo);
    document.body.append(form);
    form.submit();
    return true;
  }

  function portalReturnUrl() {
    return new URL("./", window.location.href).toString();
  }

  function readNasSsoState() {
    try {
      const value = JSON.parse(sessionStorage.getItem(nasSsoKey));
      return value && Array.isArray(value.sessions) ? value : null;
    } catch {
      return null;
    }
  }

  function writeNasSsoState(value) {
    sessionStorage.setItem(nasSsoKey, JSON.stringify(value));
  }

  function clearNasSsoState() {
    sessionStorage.removeItem(nasSsoKey);
  }

  function firstNasLogoutUrl() {
    const state = readNasSsoState();
    return state?.sessions?.find((item) => item.ready)?.logoutUrl ?? "";
  }

  async function rest(path) {
    const response = await fetch(`${config.supabaseUrl}/rest/v1/${path}`, {
      headers: { ...authenticatedHeaders(), accept: "application/json" },
    });
    const body = await readResponse(response);
    if (!response.ok) throw new Error(body.message || "데이터를 불러오지 못했습니다.");
    return body;
  }

  async function functionRequest(path, body) {
    const response = await fetch(`${config.functionBaseUrl}${path}`, {
      method: "POST",
      headers: authenticatedHeaders(),
      body: JSON.stringify(body),
    });
    const result = await readResponse(response);
    if (!response.ok) throw new Error(errorMessage(result.error_code));
    return result;
  }

  async function functionGet(path) {
    const response = await fetch(`${config.functionBaseUrl}${path}`, {
      method: "GET",
      headers: authenticatedHeaders(),
    });
    const result = await readResponse(response);
    if (!response.ok) throw new Error(errorMessage(result.error_code));
    return result;
  }

  async function mediaRequest(baseUrl, path, body, options = {}) {
    return await serviceRequest(baseUrl, path, {
      ...options,
      method: "POST",
      body: JSON.stringify(body),
    });
  }

  async function serviceRequest(baseUrl, path, options = {}) {
    const normalizedBase = baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`;
    const response = await fetch(`${normalizedBase}${path}`, {
      ...options,
      headers: { ...authenticatedHeaders(), ...(options.headers ?? {}) },
    });
    const result = await readResponse(response);
    if (!response.ok) throw new Error(errorMessage(result.error_code));
    return result;
  }

  function apiHeaders() {
    return { apikey: config.publishableKey, "content-type": "application/json" };
  }

  function authenticatedHeaders() {
    if (!session?.accessToken) throw new Error("로그인이 필요합니다.");
    return { ...apiHeaders(), authorization: `Bearer ${session.accessToken}` };
  }

  async function readResponse(response) {
    const text = await response.text();
    if (!text) return {};
    try { return JSON.parse(text); } catch { return {}; }
  }

  function readSession() {
    try { return JSON.parse(sessionStorage.getItem(sessionKey)); } catch { return null; }
  }

  function clearSession() {
    session = null;
    sessionStorage.removeItem(sessionKey);
  }

  function setBusy(button, busy, label) {
    button.disabled = busy;
    button.textContent = label;
  }

  function setConnection(label, state) {
    elements.connectionBadge.textContent = label;
    elements.connectionBadge.className = `status-pill status-${state}`;
  }

  function setClaimState(state, icon, title, description) {
    elements.claimStatePanel.className = `claim-state state-${state}`;
    elements.claimStateIcon.textContent = icon;
    elements.claimStateTitle.textContent = title;
    elements.claimStateDescription.textContent = description;
  }

  function showNotice(message, type) {
    elements.notice.textContent = message;
    elements.notice.className = `notice notice-${type}`;
    elements.notice.hidden = false;
  }

  function hideNotice() {
    elements.notice.hidden = true;
  }

  function statusText(value) {
    const labels = {
      connected: "연결됨",
      disconnected: "연결 안 됨",
      ready: "준비됨",
      pending: "준비 중",
      storage_error: "저장소 오류",
      unknown: "확인 필요",
    };
    return labels[value] ?? value ?? "확인 필요";
  }

  function errorMessage(code) {
    const messages = {
      operation_not_authorized: "이 조직의 기기를 승인할 권한이 없습니다.",
      invalid_publishable_key: "포털 공개 키 설정을 확인하세요.",
      database_operation_failed: "서버가 요청을 처리하지 못했습니다.",
      route_not_found: "등록 API 경로를 찾지 못했습니다.",
      authorization_required: "로그인 세션을 확인하세요.",
      camera_not_found: "카메라를 찾을 수 없거나 접근 권한이 없습니다.",
      recording_not_found: "녹화영상을 찾을 수 없거나 아직 준비되지 않았습니다.",
      ingress_preparing: "실시간 송출을 준비하고 있습니다. 잠시 후 다시 시도하세요.",
      ingress_creation_failed: "실시간 송출 채널을 만들지 못했습니다.",
      livekit_not_configured: "실시간 서버 설정이 완료되지 않았습니다.",
      nas_location_not_configured: "NAS 다운로드 위치 설정이 완료되지 않았습니다.",
      nas_signing_key_not_configured: "NAS 통합 로그인 서명 설정이 완료되지 않았습니다.",
      nas_session_scope_not_found: "이 계정에 허용된 NAS 다운로드 범위가 없습니다.",
      nas_session_creation_failed: "NAS 통합 로그인을 준비하지 못했습니다.",
      invalid_nas_location: "NAS 다운로드 주소 설정이 올바르지 않습니다.",
      invalid_cursor: "녹화영상 목록 위치가 만료됐습니다. 목록을 새로고침하세요.",
      server_not_configured: "미디어 서버 설정이 완료되지 않았습니다.",
      internal_error: "서버가 요청을 처리하지 못했습니다.",
    };
    return messages[code] ?? "요청을 처리하지 못했습니다.";
  }
})();
