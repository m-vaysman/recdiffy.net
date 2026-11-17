// Counts the network requests this page makes after the engine has loaded, and lists each one.
// Your files never appear here: they are read in this tab and never sent anywhere.
(() => {
    let ready = false;
    const sent = [];

    const render = () => {
        const count = document.getElementById('net-count');
        const list = document.getElementById('net-list');
        if (!count || !list) return;
        count.textContent = ready ? String(sent.length) : '…';
        list.replaceChildren(...sent.map(name => {
            const item = document.createElement('li');
            item.textContent = name;
            return item;
        }));
    };

    new PerformanceObserver(entries => {
        if (!ready) return;
        for (const entry of entries.getEntries()) sent.push(entry.name);
        render();
    }).observe({ type: 'resource' });

    window.recdiffyReady = () => {
        ready = true;
        render();
    };

    render();
})();
