// U盘文件复制器 - Web管理面板
const API_BASE = window.location.origin + '/api/file';
const HEALTH_URL = window.location.origin + '/api/health';

// ============ 令牌管理 ============
function getToken() {
    let token = localStorage.getItem('api_token');
    if (!token) {
        token = prompt('请输入API令牌 (Bearer Token)', '1145141919810');
        if (token) localStorage.setItem('api_token', token);
        else throw new Error('缺少令牌');
    }
    return token;
}

function setToken() {
    const token = prompt('请输入API令牌', localStorage.getItem('api_token') || '');
    if (token !== null) {
        localStorage.setItem('api_token', token);
        testConnection();
    }
}

// ============ API 请求 ============
async function apiRequest(url, options = {}) {
    const token = getToken();
    const headers = {
        'Authorization': `Bearer ${token}`,
        ...options.headers
    };
    const resp = await fetch(url, { ...options, headers });
    if (resp.status === 401) {
        localStorage.removeItem('api_token');
        alert('认证失败，请重新输入令牌');
        throw new Error('Unauthorized');
    }
    if (!resp.ok) {
        const text = await resp.text();
        throw new Error(`HTTP ${resp.status}: ${text}`);
    }
    return resp;
}

// ============ 连接状态 ============
async function testConnection() {
    const dot = document.getElementById('connStatus');
    const text = document.getElementById('connText');
    try {
        const resp = await fetch(HEALTH_URL);
        if (resp.ok) {
            dot.textContent = '🟢'; text.textContent = '已连接';
            dot.style.color = 'var(--success)';
            return true;
        }
    } catch (e) {}
    dot.textContent = '🔴'; text.textContent = '未连接';
    return false;
}

// ============ 标签页切换 ============
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById('tab-' + tab.dataset.tab).classList.add('active');
        // 切换到对应标签时加载数据
        if (tab.dataset.tab === 'browse') loadFileList();
        else if (tab.dataset.tab === 'stats') loadStats();
    });
});

// ============ 文件浏览 ============
let currentPath = '';
let currentPage = 1;

async function loadFileList(path, page) {
    if (path !== undefined) currentPath = path || '';
    if (page !== undefined) currentPage = page || 1;

    const recursive = document.getElementById('recursiveToggle').checked;
    try {
        const url = `${API_BASE}/list?path=${encodeURIComponent(currentPath)}&recursive=${recursive}&page=${currentPage}&pageSize=50`;
        const resp = await apiRequest(url);
        const data = await resp.json();
        renderFileTable(data);
        renderBreadcrumb();
    } catch (e) {
        document.getElementById('fileTableBody').innerHTML =
            `<tr><td colspan="5" class="empty-msg">加载失败: ${e.message}</td></tr>`;
    }
}

function renderBreadcrumb() {
    const bc = document.getElementById('breadcrumb');
    const parts = currentPath ? currentPath.split('/').filter(p => p) : [];
    let html = '<span class="crumb" data-path="">📁 根目录</span>';
    let buildPath = '';
    parts.forEach((part, i) => {
        buildPath += (i ? '/' : '') + part;
        html += `<span class="crumb-sep">/</span><span class="crumb" data-path="${buildPath}">${part}</span>`;
    });
    bc.innerHTML = html;
    bc.querySelectorAll('.crumb').forEach(el => {
        el.addEventListener('click', () => loadFileList(el.dataset.path, 1));
    });
}

function formatSize(bytes) {
    if (bytes === 0) return '-';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

function renderFileTable(data) {
    const tbody = document.getElementById('fileTableBody');
    if (!data.items || data.items.length === 0) {
        tbody.innerHTML = '<tr><td colspan="5" class="empty-msg">目录为空</td></tr>';
        document.getElementById('browsePager').innerHTML = '';
        return;
    }

    // 分别列出目录和文件
    const dirs = data.items.filter(f => f.isDirectory);
    const files = data.items.filter(f => !f.isDirectory);
    const sorted = [...dirs, ...files];

    tbody.innerHTML = sorted.map(f => {
        const icon = f.isDirectory ? '📁' : getFileIcon(f.name);
        if (f.isDirectory) {
            const cleanPath = f.path.endsWith('/') ? f.path.slice(0, -1) : f.path;
            return `<tr>
                <td>📁</td>
                <td><span class="dir-link" data-path="${cleanPath}">${escHtml(f.name)}/</span></td>
                <td class="size-col">-</td>
                <td class="time-col">${f.lastWriteTimeUtc || ''}</td>
                <td></td>
            </tr>`;
        }
        return `<tr>
            <td>${icon}</td>
            <td>${escHtml(f.name)}</td>
            <td class="size-col">${formatSize(f.sizeBytes)}</td>
            <td class="time-col">${f.lastWriteTimeUtc || ''}</td>
            <td class="action-col">
                <button class="btn btn-sm btn-outline preview-btn" data-path="${escHtml(f.path)}">预览</button>
                <button class="btn btn-sm btn-outline download-btn" data-path="${escHtml(f.path)}">下载</button>
                <button class="btn btn-sm btn-danger delete-btn" data-path="${escHtml(f.path)}" data-name="${escHtml(f.name)}">删除</button>
            </td>
        </tr>`;
    }).join('');

    // 目录点击事件
    tbody.querySelectorAll('.dir-link').forEach(el => {
        el.addEventListener('click', () => loadFileList(el.dataset.path, 1));
    });
    // 操作按钮事件
    tbody.querySelectorAll('.preview-btn').forEach(el => {
        el.addEventListener('click', () => previewFile(el.dataset.path));
    });
    tbody.querySelectorAll('.download-btn').forEach(el => {
        el.addEventListener('click', () => downloadFile(el.dataset.path));
    });
    tbody.querySelectorAll('.delete-btn').forEach(el => {
        el.addEventListener('click', () => deleteFile(el.dataset.path, el.dataset.name));
    });

    // 分页
    renderPager('browsePager', data.total, data.page, data.pageSize, (p) => loadFileList(currentPath, p));
}

function renderPager(id, total, page, pageSize, callback) {
    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    if (totalPages <= 1) { document.getElementById(id).innerHTML = ''; return; }
    let html = `<span>共 ${total} 项</span>`;
    if (page > 1) html += `<button class="btn btn-sm btn-outline" data-page="${page-1}">上一页</button>`;
    html += `<span>${page} / ${totalPages}</span>`;
    if (page < totalPages) html += `<button class="btn btn-sm btn-outline" data-page="${page+1}">下一页</button>`;
    document.getElementById(id).innerHTML = html;
    document.getElementById(id).querySelectorAll('button').forEach(b => {
        b.addEventListener('click', () => callback(parseInt(b.dataset.page)));
    });
}

function getFileIcon(name) {
    const ext = (name || '').split('.').pop().toLowerCase();
    const map = {
        jpg: '🖼️', jpeg: '🖼️', png: '🖼️', gif: '🖼️', bmp: '🖼️', svg: '🖼️', webp: '🖼️',
        mp4: '🎬', avi: '🎬', mkv: '🎬', mov: '🎬', wmv: '🎬',
        mp3: '🎵', wav: '🎵', flac: '🎵', aac: '🎵',
        pdf: '📕', doc: '📘', docx: '📘', xls: '📗', xlsx: '📗', ppt: '📙', pptx: '📙',
        zip: '📦', rar: '📦', '7z': '📦', tar: '📦', gz: '📦',
        txt: '📄', md: '📄', log: '📄', csv: '📄', json: '📄', xml: '📄',
        html: '🌐', htm: '🌐', css: '🎨', js: '📜', ts: '📜',
        exe: '⚙️', dll: '⚙️', msi: '⚙️',
    };
    return map[ext] || '📄';
}

function escHtml(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

// ============ 预览/下载/删除 ============
async function previewFile(path) {
    const modal = document.getElementById('previewModal');
    const title = document.getElementById('previewTitle');
    const info = document.getElementById('previewInfo');
    const code = document.getElementById('previewContent');
    const img = document.getElementById('previewImage');
    const binary = document.getElementById('previewBinary');

    title.textContent = path;
    code.style.display = 'none';
    img.style.display = 'none';
    binary.style.display = 'none';
    modal.style.display = 'flex';

    const ext = (path.split('.').pop() || '').toLowerCase();
    const textExts = ['txt', 'md', 'log', 'csv', 'json', 'xml', 'html', 'htm', 'css', 'js', 'ts', 'py', 'cs', 'java', 'cpp', 'c', 'h', 'yaml', 'yml', 'ini', 'cfg', 'conf', 'sh', 'bat', 'ps1', 'sql'];
    const imgExts = ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'svg', 'webp', 'ico'];

    try {
        const url = `${API_BASE}/download?path=${encodeURIComponent(path)}`;
        const token = getToken();
        const resp = await fetch(url, { headers: { 'Authorization': `Bearer ${token}` } });
        if (!resp.ok) throw new Error('下载失败');

        const contentLength = resp.headers.get('Content-Length') || '?';
        info.innerHTML = `<span>大小: ${formatSize(parseInt(contentLength) || 0)}</span>`;

        if (imgExts.includes(ext)) {
            const blob = await resp.blob();
            img.src = URL.createObjectURL(blob);
            img.style.display = 'block';
        } else if (textExts.includes(ext) && (parseInt(contentLength) || 0) < 1024 * 1024) {
            const text = await resp.text();
            code.textContent = text;
            code.style.display = 'block';
        } else {
            binary.style.display = 'block';
            document.getElementById('downloadBtn').onclick = () => downloadFile(path);
        }
    } catch (e) {
        info.innerHTML = `<span style="color:red">错误: ${e.message}</span>`;
    }
}

function downloadFile(path) {
    const token = getToken();
    const url = `${API_BASE}/download?path=${encodeURIComponent(path)}`;
    const a = document.createElement('a');
    a.href = url;
    // 通过 fetch 方式带 token 下载
    fetch(url, { headers: { 'Authorization': `Bearer ${token}` } })
        .then(r => r.blob())
        .then(blob => {
            const objUrl = URL.createObjectURL(blob);
            a.href = objUrl;
            a.download = path.split('/').pop();
            a.click();
            URL.revokeObjectURL(objUrl);
        })
        .catch(e => alert('下载失败: ' + e.message));
}

async function deleteFile(path, name) {
    if (!confirm(`确定删除 "${name}" 吗？此操作不可撤销！`)) return;
    try {
        const url = `${API_BASE}/file?path=${encodeURIComponent(path)}`;
        await apiRequest(url, { method: 'DELETE' });
        alert('删除成功');
        loadFileList();
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}

// 弹窗关闭
document.getElementById('modalClose').addEventListener('click', () => {
    document.getElementById('previewModal').style.display = 'none';
});
document.getElementById('previewModal').addEventListener('click', (e) => {
    if (e.target.id === 'previewModal') e.target.style.display = 'none';
});

// ============ 文件上传 ============
const dropZone = document.getElementById('dropZone');
const uploadFileInput = document.getElementById('uploadFileInput');
const uploadPath = document.getElementById('uploadPath');
let selectedUploadFile = null;

dropZone.addEventListener('click', () => uploadFileInput.click());
uploadFileInput.addEventListener('change', (e) => {
    selectedUploadFile = e.target.files[0];
    if (selectedUploadFile) {
        document.getElementById('dropFileInfo').textContent =
            `${selectedUploadFile.name} (${formatSize(selectedUploadFile.size)})`;
        document.getElementById('uploadBtn').disabled = false;
    }
});

dropZone.addEventListener('dragover', (e) => { e.preventDefault(); dropZone.classList.add('drag-over'); });
dropZone.addEventListener('dragleave', () => dropZone.classList.remove('drag-over'));
dropZone.addEventListener('drop', (e) => {
    e.preventDefault();
    dropZone.classList.remove('drag-over');
    selectedUploadFile = e.dataTransfer.files[0];
    if (selectedUploadFile) {
        document.getElementById('dropFileInfo').textContent =
            `${selectedUploadFile.name} (${formatSize(selectedUploadFile.size)})`;
        document.getElementById('uploadBtn').disabled = false;
        if (!uploadPath.value) uploadPath.value = selectedUploadFile.name;
    }
});

document.getElementById('uploadBtn').addEventListener('click', async () => {
    const path = uploadPath.value.trim();
    if (!path) { alert('请输入目标路径'); return; }
    if (!selectedUploadFile) { alert('请选择文件'); return; }

    const btn = document.getElementById('uploadBtn');
    const prog = document.getElementById('uploadProgress');
    btn.disabled = true;
    prog.textContent = '上传中...';
    try {
        const url = `${API_BASE}/file?path=${encodeURIComponent(path)}`;
        await apiRequest(url, { method: 'PUT', body: selectedUploadFile });
        prog.textContent = '上传成功！';
        alert('上传成功');
        uploadPath.value = '';
        selectedUploadFile = null;
        uploadFileInput.value = '';
        document.getElementById('dropFileInfo').textContent = '';
    } catch (e) {
        prog.textContent = `上传失败: ${e.message}`;
    } finally {
        btn.disabled = !selectedUploadFile;
    }
});

// ============ 分块上传 ============
document.getElementById('chunkFileInput').addEventListener('change', (e) => {
    document.getElementById('chunkUploadBtn').disabled = !e.target.files.length;
});

document.getElementById('chunkUploadBtn').addEventListener('click', async () => {
    const path = document.getElementById('chunkPath').value.trim();
    const file = document.getElementById('chunkFileInput').files[0];
    const chunkSizeMB = parseFloat(document.getElementById('chunkSizeMB').value);
    if (!path) { alert('请输入目标路径'); return; }
    if (!file) { alert('请选择文件'); return; }

    const btn = document.getElementById('chunkUploadBtn');
    const prog = document.getElementById('chunkProgress');
    btn.disabled = true;
    const chunkSize = chunkSizeMB * 1024 * 1024;
    const totalChunks = Math.ceil(file.size / chunkSize);

    // 查询已上传分块
    let uploaded = new Set();
    try {
        const resp = await apiRequest(`${API_BASE}/chunk-status?path=${encodeURIComponent(path)}`);
        uploaded = new Set(await resp.json());
    } catch (e) {}

    for (let i = 0; i < totalChunks; i++) {
        if (uploaded.has(i)) {
            prog.textContent = `分块 ${i+1}/${totalChunks} 已存在，跳过...`;
            continue;
        }
        const start = i * chunkSize;
        const end = Math.min(start + chunkSize, file.size);
        const chunk = file.slice(start, end);
        const chunkUrl = `${API_BASE}/chunk?path=${encodeURIComponent(path)}&index=${i}&total=${totalChunks}`;
        const resp = await apiRequest(chunkUrl, {
            method: 'PUT',
            body: chunk,
            headers: { 'Content-Type': 'application/octet-stream' }
        });
        if (!resp.ok) { prog.textContent = `分块 ${i} 上传失败`; btn.disabled = false; return; }
        prog.textContent = `上传分块 ${i+1}/${totalChunks} 完成`;
    }

    prog.textContent = '正在合并文件...';
    const mergeUrl = `${API_BASE}/merge?path=${encodeURIComponent(path)}&total=${totalChunks}`;
    const mergeResp = await apiRequest(mergeUrl, { method: 'POST' });
    if (mergeResp.ok) {
        prog.textContent = '分块上传完成！';
        alert('分块上传并合并成功');
    } else {
        prog.textContent = '合并失败';
    }
    btn.disabled = false;
});

// ============ 文件搜索 ============
let searchPage = 1;
let lastSearchParams = null;

document.getElementById('searchBtn').addEventListener('click', () => doSearch(1));

document.getElementById('searchKeyword').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') doSearch(1);
});

async function doSearch(page) {
    const keyword = document.getElementById('searchKeyword').value.trim();
    const extension = document.getElementById('searchExt').value.trim();
    const startDate = document.getElementById('searchStart').value;
    const endDate = document.getElementById('searchEnd').value;

    lastSearchParams = { keyword, extension, startDate, endDate };
    searchPage = page;

    try {
        const params = new URLSearchParams({ keyword, extension, recursive: 'true', page, pageSize: '50' });
        if (startDate) params.set('startDate', startDate);
        if (endDate) params.set('endDate', endDate);

        const url = `${API_BASE}/search?${params.toString()}`;
        const resp = await apiRequest(url);
        const data = await resp.json();

        document.getElementById('searchResults').style.display = data.items && data.items.length ? '' : 'none';
        const tbody = document.getElementById('searchTableBody');
        tbody.innerHTML = (data.items || []).map(f => `
            <tr>
                <td>${getFileIcon(f.name)} ${escHtml(f.name)}</td>
                <td style="font-size:12px;color:var(--text-muted)">${escHtml(f.path)}</td>
                <td style="text-align:right">${formatSize(f.sizeBytes)}</td>
                <td style="font-size:13px;color:var(--text-muted)">${f.lastWriteTimeUtc || ''}</td>
                <td class="action-col">
                    <button class="btn btn-sm btn-outline preview-btn" data-path="${escHtml(f.path)}">预览</button>
                    <button class="btn btn-sm btn-outline download-btn" data-path="${escHtml(f.path)}">下载</button>
                </td>
            </tr>
        `).join('') || '<tr><td colspan="5" class="empty-msg">未找到匹配文件</td></tr>';

        tbody.querySelectorAll('.preview-btn').forEach(el => {
            el.addEventListener('click', () => previewFile(el.dataset.path));
        });
        tbody.querySelectorAll('.download-btn').forEach(el => {
            el.addEventListener('click', () => downloadFile(el.dataset.path));
        });

        renderPager('searchPager', data.total, data.page, data.pageSize, doSearch);
    } catch (e) {
        document.getElementById('searchTableBody').innerHTML =
            `<tr><td colspan="5" class="empty-msg">搜索失败: ${e.message}</td></tr>`;
        document.getElementById('searchResults').style.display = '';
    }
}

// ============ 存储统计 ============
async function loadStats() {
    try {
        const resp = await apiRequest(`${API_BASE}/stats`);
        const data = await resp.json();
        document.getElementById('statFiles').textContent = (data.totalFiles || 0).toLocaleString();
        document.getElementById('statSize').textContent =
            data.totalSizeMB ? data.totalSizeMB + ' MB' : formatSize(data.totalSizeBytes || 0);
        document.getElementById('statFree').textContent =
            data.availableDiskMB ? data.availableDiskMB + ' MB' : '-';
        const totalDiskMB = data.totalDiskMB || 0;
        const usedDiskMB = totalDiskMB - (data.availableDiskMB || 0);
        const pct = totalDiskMB > 0 ? ((usedDiskMB / totalDiskMB) * 100).toFixed(1) : 0;
        document.getElementById('statUsage').textContent = pct + '%';
        document.getElementById('statChunks').textContent = (data.pendingChunks || 0).toLocaleString();
    } catch (e) {
        console.error('获取统计失败:', e);
    }
}

document.getElementById('cleanupBtn').addEventListener('click', async () => {
    const msg = document.getElementById('cleanupMsg');
    msg.textContent = '清理中...';
    try {
        const resp = await apiRequest(`${API_BASE}/cleanup?hoursOld=24`, { method: 'POST' });
        const data = await resp.json();
        msg.textContent = `清理完成，移除了 ${data.cleanedChunks || 0} 个过期分块`;
    } catch (e) {
        msg.textContent = `清理失败: ${e.message}`;
    }
});

// ============ 初始化 ============
document.getElementById('connBtn').addEventListener('click', testConnection);
document.getElementById('tokenBtn').addEventListener('click', setToken);
document.getElementById('refreshBtn').addEventListener('click', () => loadFileList());
document.getElementById('recursiveToggle').addEventListener('change', () => loadFileList());

// 初始加载
(async () => {
    const ok = await testConnection();
    if (ok) { loadFileList(); loadStats(); }
})();
