document.getElementById('loginForm').addEventListener('submit', async (e) => {
  e.preventDefault();
  const btn = e.target.querySelector('button');
  const error = document.getElementById('error');
  error.style.display = 'none';
  btn.disabled = true;

  try {
    const res = await fetch('/security/createToken', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userName: document.getElementById('userName').value,
        password: document.getElementById('password').value,
      }),
    });

    if (!res.ok) {
      error.textContent = 'Invalid credentials';
      error.style.display = 'block';
      return;
    }

    const token = await res.json();
    // Exchange the app's bearer token for the dashboard's own session cookie, then open the dashboard.
    // The token never goes into a URL or browser storage; the cookie is HttpOnly and lasts until the
    // dashboard session's idle timeout or absolute lifetime ends.
    const session = await fetch('/messaging/dashboard/api/auth/session', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'X-Requested-With': 'XMLHttpRequest',
      },
    });

    if (session.status !== 204) {
      error.textContent = session.status === 403 ? 'Not allowed to view the dashboard' : 'Dashboard sign-in failed';
      error.style.display = 'block';
      return;
    }

    window.location.assign('/messaging/dashboard/');
  } catch {
    error.textContent = 'Request failed';
    error.style.display = 'block';
  } finally {
    btn.disabled = false;
  }
});
