import java.awt.*;
import java.awt.geom.Rectangle2D;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import javax.swing.*;

// An isolated editor owned by the test. Does not open user files or change JAB settings.
public final class JavaCaretFixture {
    public static void main(String[] args) throws Exception {
        Path output = Paths.get(args[0]);
        SwingUtilities.invokeAndWait(() -> {
            JFrame frame = new JFrame("InputBeacon Java caret test");
            JTextArea text = new JTextArea("first line has enough characters\nsecond line\nlast line", 6, 42);
            text.setFont(new Font("Monospaced", Font.PLAIN, 18));
            frame.add(text);
            frame.pack();
            frame.setLocation(180, 180);
            frame.setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
            frame.setVisible(true);
            text.requestFocusInWindow();
            long start = System.nanoTime();
            Timer timer = new Timer(120, event -> {
                try {
                    int phase = (int)((System.nanoTime() - start) / 1000000000L / 3);
                    if (phase >= 6) { frame.dispose(); System.exit(0); }
                    if (phase == 4 && text.getDocument().getLength() != 0) text.setText("");
                    if (phase == 5 && text.getDocument().getLength() == 0) text.setText("restored\nnew line");
                    int[] positions = { 1, 18, 36, 53, 0, 12 };
                    int index = Math.min(positions[phase], text.getDocument().getLength());
                    if (text.getCaretPosition() != index) text.setCaretPosition(index);
                    Rectangle2D r = text.modelToView2D(index);
                    Point origin = text.getLocationOnScreen();
                    String geometry = phase + "," + (origin.x + (int)r.getX()) + "," + (origin.y + (int)r.getY()) + "," + (int)r.getHeight();
                    Files.write(output, geometry.getBytes(StandardCharsets.UTF_8));
                } catch (Exception error) { throw new RuntimeException(error); }
            });
            timer.start();
        });
    }
}
