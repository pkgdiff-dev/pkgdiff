module.exports = [
  {
    context: ['/package'],
    target: 'http://localhost:4200',
    bypass: function (req) {
      if (req.headers.accept && req.headers.accept.includes('text/html')) {
        return '/index.html';
      }
    },
  },
];
