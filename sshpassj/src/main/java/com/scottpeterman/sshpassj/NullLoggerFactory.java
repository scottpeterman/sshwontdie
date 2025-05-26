package com.scottpeterman.sshpassj;

import net.schmizz.sshj.common.LoggerFactory;
import org.slf4j.Logger;

public class NullLoggerFactory implements LoggerFactory {
    @Override
    public Logger getLogger(String name) {
        return new NullLogger();
    }

    @Override
    public Logger getLogger(Class<?> clazz) {
        return new NullLogger();
    }
}
