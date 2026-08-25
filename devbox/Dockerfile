FROM wordpress:latest

# Install Node.js, npm, wp-cli, and development tools
RUN apt-get update && \
    apt-get install -y curl less subversion default-mysql-client git unzip gcc make autoconf $PHPIZE_DEPS && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    curl -O https://raw.githubusercontent.com/wp-cli/builds/gh-pages/phar/wp-cli.phar && \
    chmod +x wp-cli.phar && \
    mv wp-cli.phar /usr/local/bin/wp && \
    curl -sS https://getcomposer.org/installer | php -- --install-dir=/usr/local/bin --filename=composer && \
    pecl install xdebug opentelemetry && \
    docker-php-ext-enable opentelemetry && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Verify installations
RUN node --version && npm --version && wp --version --allow-root && composer --version

# Set working directory
WORKDIR /var/www/html
