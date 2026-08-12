(() => {
  "use strict";

  const config = window.DFBLACKBOX_CONFIG;
  const sessionKey = "dfblackbox.portal.session";
  const claimCode = new URLSearchParams(window.location.search).get("code")?.trim().toUpperCase() ?? "";
  let session = readSession();
  let organizations = [];

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
  };

  elements.loginForm.addEventListener("submit", signIn);
  elements.approvalForm.addEventListener("submit", approveClaim);
  elements.rejectButton.addEventListener("click", rejectClaim);
  elements.signOutButton.addEventListener("click", signOut);
  elements.refreshButton.addEventListener("click", loadPortal);
  elements.organizationSelect.addEventListener("change", loadSites);
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
      await showSignedIn();
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
      elements.passwordInput.value = "";
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
    await loadOrganizations();
    if (claimCode) {
      elements.approvalPanel.hidden = false;
      elements.portalPanel.hidden = true;
    } else {
      elements.approvalPanel.hidden = true;
      elements.portalPanel.hidden = false;
      await loadPortal();
    }
  }

  function showSignedOut() {
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
      setConnection("요청 거절됨", "neutral");
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      setBusy(elements.rejectButton, false, "거절");
      elements.approveButton.disabled = false;
    }
  }

  async function loadPortal() {
    setBusy(elements.refreshButton, true, "불러오는 중…");
    try {
      const cameras = await rest("cameras?select=id,display_name,camera_type,connection_state,storage_state&order=created_at.desc");
      renderCameras(cameras);
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      setBusy(elements.refreshButton, false, "새로고침");
    }
  }

  function renderCameras(cameras) {
    elements.cameraGrid.replaceChildren();
    if (!cameras.length) {
      const empty = document.createElement("div");
      empty.className = "empty-state";
      empty.innerHTML = "<strong>등록된 카메라가 없습니다.</strong><span>DFBlackbox에서 기기 등록을 시작하세요.</span>";
      elements.cameraGrid.append(empty);
      return;
    }
    for (const camera of cameras) {
      const card = elements.cameraCardTemplate.content.cloneNode(true);
      card.querySelector('[data-field="name"]').textContent = camera.display_name;
      card.querySelector('[data-field="type"]').textContent = camera.camera_type;
      card.querySelector('[data-field="connection"]').textContent = statusText(camera.connection_state);
      card.querySelector('[data-field="storage"]').textContent = statusText(camera.storage_state);
      elements.cameraGrid.append(card);
    }
  }

  async function validateSession() {
    const response = await fetch(`${config.supabaseUrl}/auth/v1/user`, {
      headers: authenticatedHeaders(),
    });
    if (!response.ok) throw new Error("invalid_session");
  }

  async function signOut() {
    if (session?.accessToken) {
      await fetch(`${config.supabaseUrl}/auth/v1/logout?scope=local`, {
        method: "POST",
        headers: authenticatedHeaders(),
      }).catch(() => undefined);
    }
    clearSession();
    hideNotice();
    showSignedOut();
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
    };
    return messages[code] ?? "요청을 처리하지 못했습니다.";
  }
})();
