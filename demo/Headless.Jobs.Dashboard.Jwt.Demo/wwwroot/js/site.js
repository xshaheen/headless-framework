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
    window.open(`/jobs/dashboard/login?access_token=Bearer ${token}`, '_blank');
  } catch {
    error.textContent = 'Request failed';
    error.style.display = 'block';
  } finally {
    btn.disabled = false;
  }
});
