using YTSPlayer.Contracts.Playback;

namespace YTSPlayer.Tests;

public class ShuffleOrderTests
{
    [Fact]
    public void Erster_Titel_ist_der_gewaehlte()
    {
        var s = new ShuffleOrder(new Random(1));
        s.Start(10, 4);
        Assert.Equal(4, s.Current);
    }

    [Fact]
    public void Lange_nicht_gespielte_Titel_sind_wahrscheinlicher_als_gerade_gespielte()
    {
        // Start 3 Titel, A=0 läuft. Nach dem 2. Titel hat der dritte Alter 5, der erste nur 1: er muss deutlich öfter kommen.
        int third = 0, runs = 3000;
        for (var seed = 0; seed < runs; seed++)
        {
            var s = new ShuffleOrder(new Random(seed));
            s.Start(3, 0);
            var second = s.Next();
            var next = s.Next();
            var other = Enumerable.Range(0, 3).First(i => i != 0 && i != second);
            if (next == other) third++;
        }
        Assert.InRange(third, runs * 0.7, runs);
    }

    [Fact]
    public void Nie_derselbe_Titel_zweimal_hintereinander()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var s = new ShuffleOrder(new Random(seed));
            s.Start(3, 0);
            var last = s.Current;
            for (var i = 0; i < 200; i++)
            {
                var next = s.Next();
                Assert.NotEqual(last, next);
                last = next;
            }
        }
    }

    [Fact]
    public void Zurueck_geht_im_Verlauf_und_meldet_am_Anfang_minus_eins()
    {
        var s = new ShuffleOrder(new Random(3));
        s.Start(5, 2);
        Assert.Equal(-1, s.Previous());
        var second = s.Next();
        Assert.Equal(2, s.Previous());
        Assert.Equal(second, s.Next());
    }

    [Fact]
    public void Ein_einzelner_Titel_wiederholt_sich_weil_nichts_anderes_da_ist()
    {
        var s = new ShuffleOrder(new Random(4));
        s.Start(1, 0);
        Assert.Equal(0, s.Next());
    }

    [Fact]
    public void Leere_Liste_gibt_minus_eins()
    {
        var s = new ShuffleOrder(new Random(5));
        s.Start(0, -1);
        Assert.Equal(-1, s.Next());
    }
}
